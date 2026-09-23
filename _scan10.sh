#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
echo "=== grep 'Sign' / 'Cert' / 'Token' / 'Serial' in inos files (with line) ==="
grep -rn 'Sign\|Certificate\|X509\|Serial\|Token' $(cat /tmp/inos_files.txt) 2>/dev/null | grep -iv 'design\|assign\|signal\|consign' | head -60
