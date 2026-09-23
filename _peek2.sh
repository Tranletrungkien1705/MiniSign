#!/bin/bash
# Inspect SVN pristine store of the source project
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn"
f=$(find "$BASE/pristine" -type f | head -1)
echo "FILE: $f"
file "$f"
head -c 300 "$f" | xxd | head -20
echo "=== wc.db NODES sample (.cs paths) ==="
strings "$BASE/wc.db" | grep -i "\.cs$" | head -40
