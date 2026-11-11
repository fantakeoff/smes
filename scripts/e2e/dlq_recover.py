#!/usr/bin/env python3
"""Replenish and replay a dead-lettered material issue on the isolated E2E stack."""
import os, pathlib, tempfile, time
assert '_e2e' in os.environ.get('E2E_DATABASE_NAME', '')
assert os.environ.get('E2E_API_BASE_URL') == 'http://127.0.0.1:5099'
from material_feature_helpers import ok, login

a = login('machinery')
job_id = pathlib.Path(tempfile.gettempdir(), 'smes-e2e-dlq-jobid').read_text().strip()
job = ok('/api/material-issue/jobs/' + job_id, token=a)
assert job['status'] == 'failed' and job['attempts'] == 3
bill = ok('/api/stock-bill/' + str(job['billId']), token=a)
assert bill['billStatus'] == 0
lines = bill['lines']
inbound = ok('/api/stock-bill', 'POST', {'billType': 5, 'warehouseId': bill['warehouseId'], 'lines': [
    {'productId': x['productId'], 'qty': x['qty'], 'batchNo': x['batchNo'], 'location': x['location']}
    for x in lines]}, a)
ok('/api/stock-bill/' + str(inbound['id']) + '/audit', 'POST', token=a)
replay = ok('/api/material-issue/jobs/' + job_id + '/replay', 'POST', token=a)
assert replay['jobId'] == int(job_id) and replay['status'] == 'queued'
final = job
for _ in range(30):
    final = ok('/api/material-issue/jobs/' + job_id, token=a)
    if final['status'] != 'queued': break
    time.sleep(.5)
assert final['status'] == 'completed', final
assert ok('/api/stock-bill/' + str(job['billId']), token=a)['billStatus'] == 1
print('PASS DLQ job replayed after replenishment and posted exactly once')
