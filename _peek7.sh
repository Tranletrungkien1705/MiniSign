#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn"
echo "=== Dev20 sign/cert .cs paths ==="
grep -a -o '[A-Za-z0-9_./-]*\.cs' "$BASE/wc.db" | sort -u | grep -iE 'sign|cert|token|hsm|rsa|verify|serial|kyso|chungthu' | grep -viE 'HelpPage|jquery|Content/themes' | head -80
