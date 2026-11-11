#!/usr/bin/env python3
"""Idempotent mobile-operation smoke checks; refuses non-_e2e targets."""
import os, sys, time, uuid

assert '_e2e' in os.environ.get('E2E_DATABASE_NAME', ''), 'Refusing non-E2E database'
assert os.environ.get('E2E_API_BASE_URL') == 'http://127.0.0.1:5099', 'Refusing unexpected API URL'
sys.path.insert(0, os.path.dirname(__file__))
from material_feature_helpers import ok, req, login

token = login('machinery')
work_orders = ok('/api/work-order?page=1&pageSize=500', token=token)['list']
ready = next(x for x in work_orders if x['code'] == 'E2E_MACHINERY_WO_READY')
detail = ok('/api/work-order/' + str(ready['id']), token=token)
materials = detail.get('materials') or detail.get('workOrder', {}).get('materials') or []
assert materials, 'E2E work order must have BOM material'
material = materials[0]
warehouse = next(x for x in ok('/api/warehouse/options', token=token) if x['code'] == 'E2E_MACHINERY_WH')

# The create-draft API must return the same bill for a stable key, including
# after the first reservation has been written, and reject key/payload reuse.
issue_key = 'E2E-MOBILE-' + uuid.uuid4().hex
issue_payload = {'warehouseId': warehouse['id'], 'remark': 'E2E mobile idempotency',
                 'items': [{'materialId': material['id'], 'qty': 0.001, 'batchNo': '', 'location': ''}]}
first = ok('/api/work-order/' + str(ready['id']) + '/issue', 'POST', issue_payload, token, issue_key)
second = ok('/api/work-order/' + str(ready['id']) + '/issue', 'POST', issue_payload, token, issue_key)
assert first['billId'] == second['billId'] and first['code'] == second['code'], (first, second)
changed = dict(issue_payload)
changed['items'] = [{'materialId': material['id'], 'qty': 0.002, 'batchNo': '', 'location': ''}]
status, body = req('/api/work-order/' + str(ready['id']) + '/issue', 'POST', changed, token, issue_key)
assert status != 200 or body.get('code') != 0, 'same key with changed draft unexpectedly accepted'
ok('/api/material-issue/' + str(first['billId']) + '/void', 'POST', token=token)
print('PASS material draft idempotency, changed-payload conflict, void')

# Work-report retries resolve to the original durable job; a changed payload
# cannot silently reuse the same job idempotency key.
report_key = 'E2E-MOBILE-' + uuid.uuid4().hex
report_payload = {'workOrderId': ready['id'], 'processId': None, 'workerId': None,
                  'qty': 0.001, 'qualifiedQty': 0.001, 'defectQty': 0,
                  'workHours': 0.01, 'defectItemId': None, 'remark': 'E2E mobile retry'}
report = ok('/api/work-report', 'POST', report_payload, token, report_key)
report_retry = ok('/api/work-report', 'POST', report_payload, token, report_key)
assert report['jobId'] == report_retry['jobId']
changed_report = dict(report_payload, qty=0.002, qualifiedQty=0.002)
status, body = req('/api/work-report', 'POST', changed_report, token, report_key)
assert status != 200 or body.get('code') != 0, 'same key with changed report unexpectedly accepted'
final = None
for _ in range(40):
    final = ok('/api/work-report/jobs/' + str(report['jobId']), token=token)
    if final['status'] in ('completed', 'failed'):
        break
    time.sleep(0.5)
assert final and final['status'] == 'completed', final
print('PASS work-report retry returns same completed job and rejects changed payload')

# Repair creation/handling and maintenance execution remain safe under an
# uncertain HTTP response and exact same-payload replay.
equipment_rows = ok('/api/equipment?page=1&pageSize=100', token=token)['list']
equipment = next(x for x in equipment_rows if x.get('id'))
repair_key = 'E2E-MOBILE-' + uuid.uuid4().hex
repair_payload = {'equipmentId': equipment['id'], 'faultDesc': 'E2E mobile retry fault',
                  'level': 1, 'remark': 'E2E mobile retry'}
repair = ok('/api/repair-order', 'POST', repair_payload, token, repair_key)
repair_retry = ok('/api/repair-order', 'POST', repair_payload, token, repair_key)
assert repair['id'] == repair_retry['id'] and repair['code'] == repair_retry['code']
status, body = req('/api/repair-order', 'POST', dict(repair_payload, faultDesc='different fault'), token, repair_key)
assert status != 200 or body.get('code') != 0, 'same key with changed repair unexpectedly accepted'
handle_payload = {'solution': 'E2E confirmed safe', 'repairStatus': 2, 'cost': 0.01, 'downtimeMinutes': 1}
handled = ok('/api/repair-order/' + str(repair['id']) + '/handle', 'POST', handle_payload, token)
handled_retry = ok('/api/repair-order/' + str(repair['id']) + '/handle', 'POST', handle_payload, token)
assert handled['repairStatus'] == handled_retry['repairStatus'] == 2
print('PASS repair create/handle idempotent replay and changed-payload conflict')

task = ok('/api/maintenance-task', 'POST', {'equipmentId': equipment['id'], 'maintenanceType': 1,
    'taskDate': '2026-11-05', 'taskStatus': 0, 'items': 'E2E check'}, token)
execute_payload = {'result': 'E2E normal', 'cost': 0.01, 'remark': 'E2E mobile retry'}
executed = ok('/api/maintenance-task/' + str(task['id']) + '/execute', 'POST', execute_payload, token)
executed_retry = ok('/api/maintenance-task/' + str(task['id']) + '/execute', 'POST', execute_payload, token)
assert executed['taskStatus'] == executed_retry['taskStatus'] == 1
readback = ok('/api/maintenance-task/' + str(task['id']), token=token)
assert readback['taskStatus'] == 1 and readback['executorId'] is not None
status, body = req('/api/maintenance-task/' + str(task['id']) + '/execute', 'POST',
                   dict(execute_payload, cost=0.02), token)
assert status != 200 or body.get('code') != 0, 'completed maintenance task unexpectedly overwritten'
print('PASS maintenance execution retry/readback and overwrite blocked')
print('ALL mobile reconciliation smoke checks passed (isolated E2E only)')
