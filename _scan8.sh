#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
echo "=== files with 'namespace idn.iNOS' ==="
grep -rl 'namespace idn.iNOS' . 2>/dev/null > /tmp/inos_files.txt
wc -l /tmp/inos_files.txt
echo "=== of those, mentioning sign/cert/token ==="
grep -li 'sign\|cert\|chữ ký\|chu ky\|chung thu\|x509\|token\|serial' $(cat /tmp/inos_files.txt) 2>/dev/null | head -60
