import json, os, urllib.request, urllib.error
assert '_e2e' in os.environ.get('E2E_DATABASE_NAME', '')
assert os.environ.get('E2E_API_BASE_URL') == 'http://127.0.0.1:5099'
base = os.environ['E2E_API_BASE_URL']

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

def login(tenant):
    password = os.environ['E2E_PASSWORD']
    return ok('/api/login', 'POST', {'tenantCode': tenant, 'username': tenant + '_operator', 'password': password})['accessToken']
