#!/usr/bin/env python3
"""Isolated-only API smoke test. Never runs against a non-_e2e database."""
import json, os, time, urllib.request, urllib.error, uuid

assert '_e2e' in os.environ.get('E2E_DATABASE_NAME', ''), 'Refusing non-E2E database'
base = os.environ.get('E2E_API_BASE_URL', '')
assert base.startswith('http://127.0.0.1:5099'), 'Refusing unexpected API URL'


def req(path, method='GET', payload=None, token=None, key=None):
    headers = {'Content-Type': 'application/json'}
    if token: headers['Authorization'] = 'Bearer ' + token
    if key: headers['Idempotency-Key'] = key
    data = json.dumps(payload).encode() if payload is not None else None
    try:
        with urllib.request.urlopen(urllib.request.Request(base + path, data=data, headers=headers, method=method), timeout=20) as resp:
            return resp.status, json.load(resp)
    except urllib.error.HTTPError as ex:
        return ex.code, json.load(ex)


def ok(path, method='GET', payload=None, token=None, key=None):
    status, body = req(path, method, payload, token, key)
    assert status == 200 and body.get('code') == 0, (path, status, body.get('code'), body.get('message'))
    return body['data']


def denied(path, method='GET', payload=None, token=None):
    status, body = req(path, method, payload, token)
    assert status != 200 or body.get('code') != 0, (path, status, body)


def wait_job(path, token):
    for _ in range(30):
        data = ok(path, token=token)
        if data['status'] in ('completed', 'failed', 'dead-lettered'): return data
        time.sleep(0.5)
    raise AssertionError(path + ' never completed')


def login(tenant):
    password = os.environ['E2E_PASSWORD']
    data = ok('/api/login', 'POST', {'tenantCode': tenant, 'username': tenant + '_operator', 'password': password})
    return data['accessToken']


status, _ = req('/api/material-issue/jobs/1')
assert status == 401
print('PASS anonymous issue status blocked')
a, b = login('machinery'), login('electronics')
orders = ok('/api/work-order?page=1&pageSize=500', token=a)['list']
ready = next(x for x in orders if x['code'] == 'E2E_MACHINERY_WO_READY')
short = next(x for x in orders if x['code'] == 'E2E_MACHINERY_WO_SHORT')
ready_detail = ok('/api/work-order/' + str(ready['id']), token=a)
mat = ready_detail['materials'][0]
warehouses = ok('/api/warehouse/options', token=a)
warehouse = next(x for x in warehouses if x['code'] == 'E2E_MACHINERY_WH')
source = warehouse['id']
stock_rows = ok('/api/inventory/stock?warehouseId=' + str(source) + '&page=1&pageSize=100', token=a)['list']
stock_before = sum(x['qty'] for x in stock_rows if x['productId'] == mat['productId'])
assert stock_before >= 2
print('PASS E2E tenant, BOM material and stock fixture')

suffix = uuid.uuid4().hex
create = {'warehouseId': source, 'items': [{'materialId': mat['id'], 'qty': 2, 'batchNo': '', 'location': ''}], 'remark': 'E2E-' + suffix}
draft = ok('/api/work-order/' + str(ready['id']) + '/issue', 'POST', create, a)
assert draft['code'].startswith('LL') and draft['status'] == 'draft'
assert ok('/api/work-order/' + str(ready['id']), token=a)['materials'][0]['issuedQty'] == mat['issuedQty']
assert sum(x['qty'] for x in ok('/api/inventory/stock?warehouseId=' + str(source) + '&page=1&pageSize=100', token=a)['list'] if x['productId'] == mat['productId']) == stock_before
print('PASS selective draft unique code and no premature stock/issued changes')

denied('/api/stock-bill/' + str(draft['billId']) + '/audit', 'POST', token=a)
denied('/api/material-issue/jobs/1', token=b)
print('PASS generic audit denied and cross-tenant job denied')

short_detail = ok('/api/work-order/' + str(short['id']), token=a)
shortmat = short_detail['materials'][0]
shortdraft = ok('/api/work-order/' + str(short['id']) + '/issue', 'POST', {'warehouseId': source, 'items': [{'materialId': shortmat['id'], 'qty': 10, 'batchNo': '', 'location': ''}]}, a)
assert shortdraft['shortageHints'] and shortdraft['code'] != draft['code']
denied('/api/material-issue/' + str(shortdraft['billId']) + '/submit', 'POST', token=a)
voided = ok('/api/material-issue/' + str(shortdraft['billId']) + '/void', 'POST', token=a)
assert voided['billId'] == shortdraft['billId']
shortdraft2 = ok('/api/work-order/' + str(short['id']) + '/issue', 'POST', {'warehouseId': source, 'items': [{'materialId': shortmat['id'], 'qty': 10, 'batchNo': '', 'location': ''}]}, a)
assert shortdraft2['billId'] != shortdraft['billId'] and shortdraft2['shortageHints']
print('PASS shortage draft, blocked posting, void and released demand reservation')

job = ok('/api/material-issue/' + str(draft['billId']) + '/submit', 'POST', token=a, key='E2E-' + suffix)
assert job['jobId'] > 0
final = wait_job('/api/material-issue/jobs/' + str(job['jobId']), a)
assert final['status'] == 'completed', final
assert ok('/api/work-order/' + str(ready['id']), token=a)['materials'][0]['issuedQty'] == mat['issuedQty'] + 2
stock_after = sum(x['qty'] for x in ok('/api/inventory/stock?warehouseId=' + str(source) + '&page=1&pageSize=100', token=a)['list'] if x['productId'] == mat['productId'])
assert stock_after == stock_before - 2
replay = ok('/api/material-issue/' + str(draft['billId']) + '/submit', 'POST', token=a, key='E2E-' + suffix)
assert replay['jobId'] == job['jobId'] and replay['status'] == 'completed'
assert sum(x['qty'] for x in ok('/api/inventory/stock?warehouseId=' + str(source) + '&page=1&pageSize=100', token=a)['list'] if x['productId'] == mat['productId']) == stock_after
print('PASS topic issue, transaction, idempotent resubmit and stock ledger')

# Quality judgement is asynchronous and the same key is safe to retry.
q = ok('/api/quality-inspection', 'POST', {'productId': mat['productId'], 'qty': 2, 'sampleQty': 2, 'result': 0, 'qualifiedQty': 0, 'defectQty': 0, 'inspectionType': 1}, a)
qkey = 'E2E-' + uuid.uuid4().hex
judge = {'result': 1, 'qualifiedQty': 2, 'defectQty': 0, 'defectItemId': None, 'remark': 'E2E quality'}
qjob = ok('/api/quality-inspection/' + str(q['id']) + '/judge', 'POST', judge, a, qkey)
qfinal = wait_job('/api/quality-inspection/jobs/' + str(qjob['jobId']), a)
assert qfinal['status'] == 'completed', qfinal
assert ok('/api/quality-inspection/' + str(q['id']), token=a)['result'] == 1
assert ok('/api/quality-inspection/' + str(q['id']) + '/judge', 'POST', judge, a, qkey)['jobId'] == qjob['jobId']
denied('/api/quality-inspection/' + str(q['id']), 'PUT', {'result': 0, 'productId': mat['productId'], 'qty': 2, 'sampleQty': 2}, a)
print('PASS topic quality judgement, idempotent retry and locked result')

supplier = next(x for x in ok('/api/supplier/options', token=a) if x['code'] == 'E2E_MACHINERY_SUP')
target = next(x for x in warehouses if x['code'] == 'E2E_MACHINERY_WH_TRANSFER')['id']
order = ok('/api/subcontract', 'POST', {'supplierId': supplier['id'], 'inputProductId': mat['productId'], 'outputProductId': ready['productId'], 'orderQty': 1, 'sourceWarehouseId': source, 'targetWarehouseId': target, 'orderDate': '2026-11-05', 'inputBatchNo': '', 'inputLocation': '', 'outputBatchNo': 'E2E-LOT', 'outputLocation': ''}, a)
assert order['code'].startswith('SW')
dispatch = ok('/api/subcontract/' + str(order['id']) + '/dispatch', 'POST', {'qty': 1, 'batchNo': '', 'location': ''}, a)
assert dispatch['issuedQty'] == 1
receipt = ok('/api/subcontract/' + str(order['id']) + '/receipt', 'POST', {'qty': 1, 'batchNo': 'E2E-LOT', 'location': ''}, a)
assert receipt['receivedQty'] == 1
denied('/api/subcontract/' + str(order['id']) + '/receipt', 'POST', {'qty': 1}, a)
print('PASS subcontract dispatch, receipt and overreceipt denied')
print('ALL feature smoke checks passed (isolated E2E stack)')
