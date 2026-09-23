#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn"
# Try to find the checksum near the SignData.cs path in wc.db
echo "=== context around SignData.cs ==="
grep -a -o '.\{0,80\}SignData\.cs.\{0,80\}' "$BASE/wc.db" | head -5
echo "=== context around RSAUtil.cs ==="
grep -a -o '.\{0,80\}RSAUtil\.cs.\{0,80\}' "$BASE/wc.db" | head -5
