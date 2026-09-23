#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn"
echo "=== exact filenames ==="
for n in SignData SignConfig RSAUtil Signature CertificateInfo SignTTHD signTTHD; do
  echo "--- $n ---"
  grep -a -o "[A-Za-z0-9_./-]*${n}[A-Za-z0-9_./-]*\.cs" "$BASE/wc.db" | sort -u | head -10
done
