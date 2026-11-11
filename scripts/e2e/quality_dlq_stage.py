#!/usr/bin/env python3
"""Stage a quality judgement to fail after broker restart (E2E only)."""
import os, pathlib, tempfile, uuid
assert '_e2e' in os.environ.get('E2E_DATABASE_NAME', '')
assert os.environ.get('E2E_API_BASE_URL') == 'http://127.0.0.1:5099'
from material_feature_helpers import ok, login
a = login('machinery')
mat = next(x for x in ok('/api/product/options', token=a) if x['code'] == 'E2E_MACHINERY_MAT')
inspection = ok('/api/quality-inspection', 'POST', {'productId': mat['id'], 'qty': 1, 'sampleQty': 1, 'result': 0, 'qualifiedQty': 0, 'defectQty': 0, 'inspectionType': 1}, a)
job = ok('/api/quality-inspection/' + str(inspection['id']) + '/judge', 'POST', {'result': 1, 'qualifiedQty': 1, 'defectQty': 0}, a, 'E2E-' + uuid.uuid4().hex)
pathlib.Path(tempfile.gettempdir(), 'smes-e2e-quality-dlq').write_text(str(inspection['id']) + ' ' + str(job['jobId']))
print('PASS staged quality job in isolated E2E while broker stopped')
