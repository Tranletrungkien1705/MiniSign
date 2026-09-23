#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn"
echo "=== .cs paths in wc.db ==="
grep -a -o '[A-Za-z0-9_./-]*\.cs' "$BASE/wc.db" | sort -u | head -80
echo "=== count ==="
grep -a -o '[A-Za-z0-9_./-]*\.cs' "$BASE/wc.db" | sort -u | wc -l
