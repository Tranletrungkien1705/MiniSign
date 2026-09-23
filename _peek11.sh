#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine"
echo "=== files containing signTTHD ==="
grep -rl 'signTTHD' "$BASE" 2>/dev/null | head
echo "=== files containing SignatureVerify ==="
grep -rl 'SignatureVerify' "$BASE" 2>/dev/null | head
echo "=== files containing CertificateInfo ==="
grep -rl 'CertificateInfo' "$BASE" 2>/dev/null | head
echo "=== files containing GetCertificateInfo ==="
grep -rl 'GetCertificateInfo' "$BASE" 2>/dev/null | head
