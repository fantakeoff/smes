#!/usr/bin/env python3
"""Force a material-issue consumer failure in isolated E2E, with broker stopped."""
import os, pathlib, tempfile
assert '_e2e' in os.environ.get('E2E_DATABASE_NAME', '')
assert os.environ.get('E2E_API_BASE_URL') == 'http://127.0.0.1:5099'
from material_feature_helpers import ok, login

a = login('machinery')
orders = ok('/api/work-order?page=1&pageSize=500', token=a)['list']
ready = next(x for x in orders if x['code'] == 'E2E_MACHINERY_WO_READY')
mat = ok('/api/work-order/' + str(ready['id']), token=a)['materials'][0]
source = next(x for x in ok('/api/warehouse/options', token=a) if x['code'] == 'E2E_MACHINERY_WH')['id']
stock = ok('/api/inventory/stock?warehouseId=' + str(source) + '&page=1&pageSize=100', token=a)['list']
available = next(x['availableQty'] for x in stock if x['productId'] == mat['productId'] and not x.get('batchNo'))
qty = min(2, available, mat['requiredQty'] - mat['issuedQty'])
assert qty > 0, 'No material stock left for DLQ E2E'
draft = ok('/api/work-order/' + str(ready['id']) + '/issue', 'POST', {'warehouseId': source, 'items': [{'materialId': mat['id'], 'qty': qty, 'batchNo': '', 'location': ''}]}, a)
job = ok('/api/material-issue/' + str(draft['billId']) + '/submit', 'POST', token=a)
bill = ok('/api/stock-bill', 'POST', {'billType': 6, 'warehouseId': source, 'billDate': '2026-11-05', 'lines': [{'productId': mat['productId'], 'qty': qty, 'batchNo': '', 'location': ''}]}, a)
ok('/api/stock-bill/' + str(bill['id']) + '/audit', 'POST', token=a)
pathlib.Path(tempfile.gettempdir(), 'smes-e2e-dlq-jobid').write_text(str(job['jobId']))
print('PASS staged queued issue and depleted its stock while isolated broker was stopped, jobId=' + str(job['jobId']))
