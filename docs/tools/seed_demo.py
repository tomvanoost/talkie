"""Exercises the Talkie API end to end and fills it with demo data (used for the manual's screenshots).

Usage: python3 docs/tools/seed_demo.py http://localhost:<web-port>
"""
import base64, json, sys, urllib.request, urllib.error, uuid

DEMO_PASSWORD = 'Demo1234!'
BASE = sys.argv[1].rstrip('/') if len(sys.argv) > 1 else 'http://localhost:5300'


def call(method, path, body=None, token=None, expect=200, raw=None, content_type='application/json'):
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(BASE + path, data=data, method=method)
    if data is not None:
        req.add_header('Content-Type', content_type)
    if token:
        req.add_header('Authorization', 'Bearer ' + token)
    try:
        with urllib.request.urlopen(req) as r:
            status, text = r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        status, text = e.code, e.read().decode()
    if status != expect:
        raise SystemExit(f'{method} {path} -> {status} (expected {expect}): {text}')
    return json.loads(text) if text and text[0] in '{["' else text


def upload(path, token, filename, content_type, payload):
    boundary = uuid.uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{filename}"\r\n'
            f'Content-Type: {content_type}\r\n\r\n').encode() + payload + f'\r\n--{boundary}--\r\n'.encode()
    return call('POST', path, token=token, raw=body, content_type=f'multipart/form-data; boundary={boundary}')


def login(email, password):
    return call('POST', '/api/auth/login', {'email': email, 'password': password})['token']


ok = lambda msg: print('  ✔', msg)

print('Auth')
call('POST', '/api/auth/login', {'email': 'owner@talkie.com', 'password': 'wrong'}, expect=400); ok('wrong password rejected')
owner = login('owner@talkie.com', 'fwkh837h@m1'); ok('owner@talkie.com logs in')
admin = login('admin@talkie.com', 'TalkieAdmin1!'); ok('admin@talkie.com logs in')
assert call('GET', '/api/account/me', token=admin)['isAdmin']; ok('admin flag')
call('POST', '/api/auth/companies', {'companyName': 'talkie', 'ownerEmail': 'x@y.com', 'ownerPassword': 'abcdefg1',
     'username': 'x', 'displayName': 'X', 'roles': []}, expect=400); ok('company name "Talkie" is reserved')

print('Talkie company')
company = call('GET', '/api/company', token=owner)
roles = {r['name']: r['id'] for r in company['roles']}
people = [('Emma', 'Jansen', 'emma@talkie.com', ['Moderator']), ('Liam', 'de Vries', 'liam@talkie.com', ['Member']),
          ('Sophie', 'Bakker', 'sophie@talkie.com', ['Member']), ('Noah', 'Visser', 'noah@talkie.com', [])]
tokens = {}
for first, last, email, rs in people:
    res = call('POST', '/api/company/members', {'firstName': first, 'lastName': last, 'email': email,
                                                'roleIds': [roles[r] for r in rs]}, token=owner)
    tokens[email] = login(email, res['temporaryPassword'])
    # Demo accounts get a known password so you can log in as them.
    call('PUT', '/api/account/password', {'currentPassword': res['temporaryPassword'], 'newPassword': DEMO_PASSWORD}, token=tokens[email], expect=204)
ok(f'added 4 members with temporary passwords (changed to {DEMO_PASSWORD})')
emma, liam, sophie, noah = (tokens[e] for e in ['emma@talkie.com', 'liam@talkie.com', 'sophie@talkie.com', 'noah@talkie.com'])
call('POST', '/api/company/members', {'firstName': 'A', 'lastName': 'B', 'email': 'c@d.com', 'roleIds': []}, token=liam, expect=403); ok('members cannot add members')

print('Servers & channels')
server = call('GET', '/api/servers', token=owner)[0]
assert server['name'] == 'Talkie'; ok('default server named after the company')
general = next(c for c in server['channels'] if c['name'] == 'general')
staff_role = call('POST', '/api/company/roles', {'name': 'Staff', 'color': '#FFCC80'}, token=owner)
ann = call('POST', f"/api/servers/{server['id']}/channels", {'name': 'Announcements', 'description': 'Company news, only moderators post here',
           'visibleToRoleIds': [], 'permissions': [
               {'roleId': None, 'canSendMessages': False, 'canDeleteMessages': False, 'canReact': True},
               {'roleId': roles['Moderator'], 'canSendMessages': True, 'canDeleteMessages': True, 'canReact': True}]}, token=owner)
staff = call('POST', f"/api/servers/{server['id']}/channels", {'name': 'staff-room', 'description': 'Private channel for staff',
             'visibleToRoleIds': [staff_role['id']], 'permissions': []}, token=owner)
ok('created #announcements (read-only for everyone) and private #staff-room')
assert not any(c['id'] == staff for c in call('GET', '/api/servers', token=liam)[0]['channels']); ok('private channel hidden from members')
call('POST', f'/api/channels/{ann}/messages', {'content': 'hi', 'replyToId': None, 'attachmentIds': []}, token=liam, expect=403); ok('send permission enforced')

def send(token, path, text, reply=None, files=None):
    return call('POST', path, {'content': text, 'replyToId': reply, 'attachmentIds': files or []}, token=token)

g = f"/api/channels/{general['id']}/messages"
m1 = send(owner, g, 'Welcome to Talkie everyone! 🎉 This is our company server.')
send(emma, g, 'Thanks! Happy to be here.')
m3 = send(liam, g, 'Who is up for lunch at 12:30? I was thinking about the new pizza place.')
send(sophie, g, 'Count me in for lunch! 🍕', reply=m3['id'])
send(noah, g, 'Reminder: the quarterly planning meeting is moved to Thursday 10:00.')
for t, e in [(emma, '🎉'), (liam, '🎉'), (sophie, '❤️'), (noah, '👍')]:
    call('POST', f"/api/messages/{m1['id']}/reactions", {'emoji': e}, token=t)
send(emma, f'/api/channels/{ann}/messages', 'The office is closed next Monday for maintenance. Enjoy the long weekend!')
ok('channel conversation with replies and reactions')

print('Direct messages & groups')
dm = call('POST', '/api/conversations/direct', {'email': 'owner@talkie.com'}, token=emma)
d = f"/api/conversations/{dm['id']}/messages"
send(emma, d, 'Hi! Do you have a minute to review the new onboarding document?')
q = send(owner, d, 'Sure, send it over.')
png = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==')
try:
    with open(sys.argv[2], 'rb') as f:
        png = f.read()
except (IndexError, OSError):
    pass
up = upload('/api/uploads', emma, 'onboarding-preview.png', 'image/png', png)
send(emma, d, 'Here is a preview of the first page 👇', reply=q['id'], files=[up['id']])
doc = upload('/api/uploads', emma, 'onboarding-v2.pdf', 'application/pdf', b'%PDF-1.4 demo')
send(emma, d, '', files=[doc['id']])
ok('direct message with image + file attachments')

grp = call('POST', '/api/conversations/group', {'name': 'Design team', 'emails': ['liam@talkie.com', 'sophie@talkie.com']}, token=emma)
gp = f"/api/conversations/{grp['id']}/messages"
send(emma, gp, 'Kick-off for the new logo tomorrow at 9!')
bad = send(liam, gp, 'oops, wrong chat')
send(sophie, gp, 'I will bring the color palette proposals 🎨')
call('DELETE', f"/api/messages/{bad['id']}", token=emma, expect=204); ok('group owner deletes a message')
call('DELETE', f"/api/messages/{m1['id']}", token=liam, expect=403); ok('cannot delete others in #general without permission')

print('Semantic search (SQL Server vector)')
hits = call('GET', '/api/search?q=lunch%20plans', token=sophie)
assert hits and 'lunch' in hits[0]['message']['content'].lower(), hits; ok(f"top hit: {hits[0]['message']['content'][:40]}…")

print('AI assistant')
call('POST', '/api/ai/ask', {'message': 'What can you do?'}, token=owner)
r = call('POST', '/api/ai/ask', {'message': 'Show my stats'}, token=owner)
assert 'Money' in r['content']; ok('assistant answers')
call('POST', '/api/ai/ask', {'message': 'search lunch'}, token=owner)

print('Shop & AFK')
call('POST', '/api/admin/grant', {'email': 'owner@talkie.com', 'money': 125000, 'shards': 2400}, token=admin, expect=204); ok('admin grants money')
call('POST', '/api/admin/grant', {'email': 'x@x.com', 'money': 1, 'shards': 1}, token=owner, expect=403); ok('only admin can grant')
for key in ['common', 'rare', 'shard']:
    box = call('POST', f'/api/shop/boxes/{key}/buy', token=owner)
    res = call('POST', f"/api/shop/boxes/{box['id']}/open", token=owner)
    call('POST', f"/api/shop/boxes/{box['id']}/open", token=owner, expect=404)
    ok(f"{key} box -> {res['amount']} {res['currency']} (cannot open twice)")
call('POST', '/api/shop/boxes/common/buy', token=owner)
call('POST', '/api/shop/shards/buy', {'amount': 50}, token=owner); call('POST', '/api/shop/shards/sell', {'amount': 20}, token=owner); ok('exchange')
call('POST', '/api/shop/effects/lightning/buy', token=owner); call('POST', '/api/shop/effects/sparkles/buy', token=owner)
call('PUT', '/api/shop/effects/active', {'key': 'lightning'}, token=owner); ok('lightning effect equipped')
call('POST', '/api/shop/boosters/x2/buy', token=owner); ok('x2 booster')
call('POST', '/api/shop/effects/galaxy/buy', token=liam, expect=400); ok('cannot buy without money')
for t, eff in [(emma, 'sparkles')]:
    call('POST', f'/api/shop/effects/{eff}/buy', token=t, expect=400)
call('POST', '/api/admin/grant', {'email': 'emma@talkie.com', 'money': 20000, 'shards': 300}, token=admin, expect=204)
call('POST', '/api/shop/effects/hearts/buy', token=emma)
call('POST', '/api/admin/grant', {'email': 'sophie@talkie.com', 'money': 8000, 'shards': 120}, token=admin, expect=204)
call('POST', '/api/shop/effects/frost/buy', token=sophie)
s = call('POST', '/api/afk/start', token=noah); assert s['isAfk']; ok('AFK started')
s = call('POST', '/api/afk/stop', token=noah); assert not s['isAfk']; ok('AFK stopped')

print('Reports')
rep = call('POST', '/api/reports', {'reportedEmail': 'noah@talkie.com', 'reason': 'Keeps posting off-topic memes in #announcements.'}, token=liam)
call('POST', '/api/reports', {'reportedEmail': 'liam@talkie.com', 'reason': 'Spammed the design group with wrong messages.'}, token=sophie)
call('DELETE', f"/api/reports/{rep['id']}", token=owner, expect=403); ok('only admin removes reports')
assert len(call('GET', '/api/reports', token=admin)) >= 2; ok('admin sees all reports')

print('Other companies')
acme = call('POST', '/api/auth/companies', {'companyName': 'Acme Corp', 'ownerEmail': 'boss@acme.com', 'ownerPassword': 'AcmeBoss123',
            'username': 'boss', 'displayName': 'Alex Boss', 'roles': [{'name': 'Engineering', 'color': '#80CBC4'}, {'name': 'Sales', 'color': '#FFAB91'}]})
assert acme['user']['isOwner']; ok('Acme Corp created with roles')
tmp = call('POST', '/api/company/members', {'firstName': 'Temp', 'lastName': 'User', 'email': 'temp@acme.com', 'roleIds': []}, token=acme['token'])
tt = login('temp@acme.com', tmp['temporaryPassword'])
call('DELETE', f"/api/company/members/{tmp['member']['id']}", token=acme['token'], expect=204)
call('GET', '/api/account/me', token=tt, expect=401); ok('deleted member (and e-mail) removed immediately')
call('POST', '/api/company/members', {'firstName': 'Temp', 'lastName': 'Again', 'email': 'temp@acme.com', 'roleIds': []}, token=acme['token']); ok('e-mail is free again')

for t in [owner, emma, liam]:
    call('POST', '/api/account/heartbeat', token=t)
print('\nAll checks passed.')
