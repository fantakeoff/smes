#!/usr/bin/env python3
"""Subcontract receipt quarantine/release E2E; refuses every non-feature target."""
import json
import os
import subprocess
import time
import urllib.error
import urllib.request
import uuid
from datetime import date

EXPECTED_DB = "smes_feature_e2e"
EXPECTED_API = "http://127.0.0.1:5099"


def require(condition, message):
    if not condition:
        raise SystemExit(message)


def connection_values(connection_string):
    values = {}
    for part in connection_string.split(";"):
        if "=" in part:
            key, value = part.split("=", 1)
            values[key.strip().lower()] = value.strip()
    return values


def connection_database(connection_string):
    values = connection_values(connection_string)
    return values.get("database") or values.get("initial catalog")


def inspect_container(name, service):
    raw = subprocess.check_output(["docker", "inspect", name], text=True)
    container = json.loads(raw)[0]
    require(container["State"]["Status"] == "running", f"Refusing: {name} is not running")
    labels = container["Config"].get("Labels") or {}
    require(labels.get("com.docker.compose.project") == "smes-feature-e2e",
            f"Refusing: {name} is not in the smes-feature-e2e Compose project")
    require(labels.get("com.docker.compose.service") == service,
            f"Refusing: {name} is not the expected {service} service")
    return container


require(os.environ.get("E2E_DATABASE_NAME") == EXPECTED_DB,
        "Refusing: E2E_DATABASE_NAME must be exactly smes_feature_e2e")
require(os.environ.get("E2E_API_BASE_URL") == EXPECTED_API,
        "Refusing: E2E_API_BASE_URL must be exactly http://127.0.0.1:5099")
db_connection = os.environ.get("E2E_DB_CONNECTION_STRING", "")
require(connection_database(db_connection) == EXPECTED_DB,
        "Refusing: E2E_DB_CONNECTION_STRING must target smes_feature_e2e")
api_container = inspect_container("smes-feature-e2e-api", "api")
mysql_container = inspect_container("smes-feature-e2e-mysql", "mysql")
api_env = api_container["Config"].get("Env") or []
mysql_env = mysql_container["Config"].get("Env") or []
api_connection = next((x.split("=", 1)[1] for x in api_env if x.startswith("ConnectionStrings__Default=")), "")
mysql_database = next((x.split("=", 1)[1] for x in mysql_env if x.startswith("MYSQL_DATABASE=")), "")
require(connection_database(api_connection) == EXPECTED_DB,
        "Refusing: feature API is not configured for smes_feature_e2e")
require(connection_values(api_connection).get("server", "").lower() == "mysql",
        "Refusing: feature API must connect to its Compose mysql service")
require(mysql_database == EXPECTED_DB,
        "Refusing: feature MySQL container is not initialized for smes_feature_e2e")
shared_networks = set(api_container["NetworkSettings"]["Networks"]) & set(mysql_container["NetworkSettings"]["Networks"])
require(any(name.startswith("smes-feature-e2e_") for name in shared_networks),
        "Refusing: feature API and MySQL do not share the isolated feature network")
base = EXPECTED_API


def request(path, method="GET", payload=None, token=None, key=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    if key:
        headers["Idempotency-Key"] = key
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(base + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as exc:
        try:
            return exc.code, json.load(exc)
        finally:
            exc.close()


def is_success(status, body):
    return status == 200 and body.get("code") == 0


def ok(path, method="GET", payload=None, token=None, key=None):
    status, body = request(path, method, payload, token, key)
    assert is_success(status, body), (path, status, body)
    return body["data"]


def login(tenant="machinery"):
    password = os.environ["E2E_PASSWORD"]
    result = ok("/api/login", "POST", {
        "tenantCode": tenant, "username": f"{tenant}_operator", "password": password
    })
    return result["accessToken"]


def wait_job(job_id, token):
    for _ in range(120):
        job = ok(f"/api/quality-inspection/jobs/{job_id}", token=token)
        if job["status"] in ("completed", "dead-lettered"):
            return job
        time.sleep(0.5)
    raise AssertionError(f"quality job {job_id} did not complete or dead-letter")


def stock_row(warehouse_id, product_id, batch_no, token):
    rows = ok(f"/api/inventory/stock?warehouseId={warehouse_id}&page=1&pageSize=500", token=token)["list"]
    matches = [x for x in rows if x["productId"] == product_id and (x.get("batchNo") or "") == batch_no]
    assert len(matches) == 1, (product_id, batch_no, matches)
    return matches[0]


def create_receipt(case, token, supplier_id, input_product_id, output_product_id,
                   source_warehouse_id, target_warehouse_id, input_batch, input_location, qty):
    suffix = uuid.uuid4().hex[:12].upper()
    lot = f"E2E-SUBQC-{case}-{suffix}"
    order = ok("/api/subcontract", "POST", {
        "name": f"E2E subcontract QC {case} {suffix}",
        "supplierId": supplier_id,
        "inputProductId": input_product_id,
        "outputProductId": output_product_id,
        "orderQty": qty,
        "sourceWarehouseId": source_warehouse_id,
        "targetWarehouseId": target_warehouse_id,
        "orderDate": date.today().isoformat(),
        "inputBatchNo": input_batch,
        "inputLocation": input_location,
        "outputBatchNo": lot,
        "outputLocation": "",
        "remark": f"E2E-{case}-{suffix}",
    }, token)
    ok(f"/api/subcontract/{order['id']}/dispatch", "POST", {
        "qty": qty, "batchNo": input_batch, "location": input_location,
        "remark": f"E2E-{case}-{suffix}"
    }, token)
    key = f"E2E-SUBQC-{case}-{suffix}"
    payload = {"qty": qty, "batchNo": lot, "location": "", "remark": f"E2E-{case}-{suffix}"}

    # Missing keys must fail before touching the order, bill, or inventory.
    before_received = ok(f"/api/subcontract/{order['id']}", token=token)["order"]["receivedQty"]
    status, body = request(f"/api/subcontract/{order['id']}/receipt", "POST", payload, token)
    assert not is_success(status, body), ("receipt without idempotency key unexpectedly succeeded", status, body)
    assert ok(f"/api/subcontract/{order['id']}", token=token)["order"]["receivedQty"] == before_received

    receipt = ok(f"/api/subcontract/{order['id']}/receipt", "POST", payload, token, key)
    assert receipt["qualityStatus"] == "pending" and receipt["quarantinedQty"] == qty
    assert receipt["inspectionId"] > 0 and receipt["billId"] > 0
    inspection = ok(f"/api/quality-inspection/{receipt['inspectionId']}", token=token)
    assert inspection["result"] == 0 and inspection["qty"] == qty and inspection["sampleQty"] == qty
    assert inspection["sourceType"] == "委外收货"
    assert inspection["sourceId"] == receipt["billId"] and inspection["sourceCode"] == receipt["code"]

    quarantined = stock_row(target_warehouse_id, output_product_id, lot, token)
    assert quarantined["qty"] == qty and quarantined["lockedQty"] == qty and quarantined["availableQty"] == 0, quarantined

    replay = ok(f"/api/subcontract/{order['id']}/receipt", "POST", payload, token, key)
    assert replay["billId"] == receipt["billId"] and replay["inspectionId"] == receipt["inspectionId"]
    unchanged = stock_row(target_warehouse_id, output_product_id, lot, token)
    assert unchanged["qty"] == qty and unchanged["lockedQty"] == qty and unchanged["availableQty"] == 0, unchanged

    changed = dict(payload, qty=qty / 2)
    status, body = request(f"/api/subcontract/{order['id']}/receipt", "POST", changed, token, key)
    assert not is_success(status, body), ("changed payload reused an idempotency key", status, body)
    return order, receipt, inspection, lot


token = login()
work_orders = ok("/api/work-order?page=1&pageSize=500", token=token)["list"]
ready = next(x for x in work_orders if x["code"] == "E2E_MACHINERY_WO_READY")
input_material = ok(f"/api/work-order/{ready['id']}", token=token)["materials"][0]
suppliers = ok("/api/supplier/options", token=token)
supplier = next(x for x in suppliers if x["code"] == "E2E_MACHINERY_SUP")
warehouses = ok("/api/warehouse/options", token=token)
source_warehouse = next(x for x in warehouses if x["code"] == "E2E_MACHINERY_WH")
target_warehouse = next(x for x in warehouses if x["code"] == "E2E_MACHINERY_WH_TRANSFER")
qty = 1
input_batch = f"E2E-SUBQC-INPUT-{uuid.uuid4().hex[:12].upper()}"
input_location = "E2E-SUBQC"
input_bill = ok("/api/stock-bill", "POST", {
    "billType": 5,
    "warehouseId": source_warehouse["id"],
    "billDate": date.today().isoformat(),
    "sourceType": "E2E subcontract quality fixture",
    "sourceCode": input_batch,
    "lines": [{
        "productId": input_material["productId"], "qty": 3,
        "batchNo": input_batch, "location": input_location,
    }],
}, token)
ok(f"/api/stock-bill/{input_bill['id']}/audit", "POST", token=token)

# PASS: only the completed asynchronous judgment releases this exact receipt lot.
pass_order, pass_receipt, pass_inspection, pass_lot = create_receipt(
    "PASS", token, supplier["id"], input_material["productId"], ready["productId"],
    source_warehouse["id"], target_warehouse["id"], input_batch, input_location, qty)
other_tenant = login("electronics")
cross_status, cross_body = request(f"/api/subcontract/{pass_order['id']}/receipt", "POST",
                                  {"qty": qty, "batchNo": pass_lot}, other_tenant,
                                  f"E2E-CROSS-TENANT-{uuid.uuid4().hex}")
assert not is_success(cross_status, cross_body), ("cross-tenant receipt unexpectedly succeeded", cross_status, cross_body)
cross_status, cross_body = request(f"/api/quality-inspection/{pass_inspection['id']}/judge", "POST",
                                  {"result": 1, "qualifiedQty": qty, "defectQty": 0}, other_tenant,
                                  f"E2E-CROSS-TENANT-JUDGE-{uuid.uuid4().hex}")
assert not is_success(cross_status, cross_body), ("cross-tenant judgment unexpectedly succeeded", cross_status, cross_body)
assert stock_row(target_warehouse["id"], ready["productId"], pass_lot, token)["lockedQty"] == qty
pass_key = f"E2E-SUBQC-JUDGE-PASS-{uuid.uuid4().hex}"
pass_request = {"result": 1, "qualifiedQty": qty, "defectQty": 0, "defectItemId": None, "remark": "E2E pass"}
pass_job = ok(f"/api/quality-inspection/{pass_inspection['id']}/judge", "POST", pass_request, token, pass_key)
assert wait_job(pass_job["jobId"], token)["status"] == "completed"
passed_inspection = ok(f"/api/quality-inspection/{pass_inspection['id']}", token=token)
assert passed_inspection["result"] == 1
released = stock_row(target_warehouse["id"], ready["productId"], pass_lot, token)
assert released["qty"] == qty and released["lockedQty"] == 0 and released["availableQty"] == qty, released
pass_replay = ok(f"/api/quality-inspection/{pass_inspection['id']}/judge", "POST", pass_request, token, pass_key)
assert pass_replay["jobId"] == pass_job["jobId"]
assert stock_row(target_warehouse["id"], ready["productId"], pass_lot, token)["lockedQty"] == 0
print("PASS async PASS releases only the receipt lot; retry is idempotent and cross-tenant writes are denied")

# FAIL judgment: completed non-PASS results leave the receipt quarantined.
_, _, fail_inspection, fail_lot = create_receipt(
    "FAIL", token, supplier["id"], input_material["productId"], ready["productId"],
    source_warehouse["id"], target_warehouse["id"], input_batch, input_location, qty)
fail_request = {"result": 2, "qualifiedQty": 0, "defectQty": qty, "defectItemId": None, "remark": "E2E fail"}
fail_job = ok(f"/api/quality-inspection/{fail_inspection['id']}/judge", "POST", fail_request,
              token, f"E2E-SUBQC-JUDGE-FAIL-{uuid.uuid4().hex}")
assert wait_job(fail_job["jobId"], token)["status"] == "completed"
assert ok(f"/api/quality-inspection/{fail_inspection['id']}", token=token)["result"] == 2
failed_stock = stock_row(target_warehouse["id"], ready["productId"], fail_lot, token)
assert failed_stock["qty"] == qty and failed_stock["lockedQty"] == qty and failed_stock["availableQty"] == 0, failed_stock
print("PASS completed FAIL judgment leaves receipt quantity locked")

# An inconsistent PASS is accepted by the current request DTO but rejected by the
# consumer; its retries dead-letter without committing judgment or releasing stock.
_, _, dlq_inspection, dlq_lot = create_receipt(
    "DLQ", token, supplier["id"], input_material["productId"], ready["productId"],
    source_warehouse["id"], target_warehouse["id"], input_batch, input_location, qty)
dlq_request = {"result": 1, "qualifiedQty": 0, "defectQty": qty, "defectItemId": None, "remark": "E2E invalid PASS"}
dlq_job = ok(f"/api/quality-inspection/{dlq_inspection['id']}/judge", "POST", dlq_request,
             token, f"E2E-SUBQC-JUDGE-DLQ-{uuid.uuid4().hex}")
dlq_status = wait_job(dlq_job["jobId"], token)
assert dlq_status["status"] == "dead-lettered", dlq_status
assert ok(f"/api/quality-inspection/{dlq_inspection['id']}", token=token)["result"] == 0
locked_after_dlq = stock_row(target_warehouse["id"], ready["productId"], dlq_lot, token)
assert locked_after_dlq["qty"] == qty and locked_after_dlq["lockedQty"] == qty and locked_after_dlq["availableQty"] == 0, locked_after_dlq
print("PASS DLQ quality job leaves inspection pending and receipt quantity locked")
print("ALL subcontract quality quarantine checks passed (smes_feature_e2e only)")
