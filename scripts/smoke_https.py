"""Exercise Identity through the actual production HTTPS reverse proxy in CI."""
import html
import http.cookiejar
import os
import re
import ssl
import urllib.parse
import urllib.request

base = os.environ["SMOKE_BASE_URL"].rstrip("/")
if not base.startswith("https://"):
    raise SystemExit("The deployment smoke test requires HTTPS.")
context = ssl.create_default_context(cafile=os.environ["SMOKE_CA_CERT"])
cookies = http.cookiejar.CookieJar()
client = urllib.request.build_opener(
    urllib.request.HTTPSHandler(context=context),
    urllib.request.HTTPCookieProcessor(cookies),
)
with client.open(base + "/", timeout=30) as response:
    assert "/Account/Login" in response.url, "Anonymous dashboard did not redirect to login"
    page = response.read().decode()
token = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page)
assert token, "Login form has no antiforgery token"
body = urllib.parse.urlencode({
    "Email": os.environ["ADMIN_EMAIL"],
    "Password": os.environ["ADMIN_PASSWORD"],
    "__RequestVerificationToken": html.unescape(token.group(1)),
}).encode()
with client.open(base + "/Account/Login", data=body, timeout=30) as response:
    assert "Business overview" in response.read().decode(), "Production login failed"
identity_cookie = [c for c in cookies if c.name == ".AspNetCore.Identity.Application"]
assert identity_cookie and identity_cookie[0].secure, "Identity cookie is not Secure"
for path, expected in [("/Users", "User management"), ("/Billing/Schedule", "Billing schedule")]:
    with client.open(base + path, timeout=30) as response:
        assert expected in response.read().decode(), "Protected page failed: " + path
print("Production HTTPS redirect, login, Secure cookie, Admin and billing pages passed.")
