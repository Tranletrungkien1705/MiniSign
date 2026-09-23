#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn"
echo "=== Dev20 .cs paths (sign/cert) ==="
grep -a -o '[A-Za-z0-9_./-]*\.cs' "$BASE/wc.db" | sort -u | grep -iE 'skycic|sign|cert|token|hsm|rsa|verify' | grep -v 'HelpPage' | head -60
echo "=== Dev20 top dirs ==="
grep -a -o 'idn\.[A-Za-z0-9_.]*' "$BASE/wc.db" | sort -u | head -30
echo "=== Dev20 all .cs count ==="
grep -a -o '[A-Za-z0-9_./-]*\.cs' "$BASE/wc.db" | sort -u | wc -l
