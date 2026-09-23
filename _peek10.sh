#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn"
echo "=== files with Sign in name (Common/ModelsUI, BizService, Utils) ==="
grep -a -o '[A-Za-z0-9_./-]*\.cs' "$BASE/wc.db" | sort -u | grep -iE 'sign' | grep -viE 'HelpPage|jquery|Content/themes|Serial' | head -60
