#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine"
for f in 2a/2a33d93443b32f6d2d7a264fe08f9d3b3b2f60ed 83/83507da5a5a32b5e37ac9df755a2752b861a5844; do
  echo "===== $f ====="
  grep -a -n 'signTTHD\|SignatureVerify\|GetCertificateInfo' "$BASE/$f.svn-base" | head -20
done
