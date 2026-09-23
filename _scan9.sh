#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
echo "=== grep 'chữ ký' / 'chu ky' / 'chung thu' with filenames ==="
grep -rli 'chữ ký\|chu ky\|chung thu\|ký số\|ky so' $(cat /tmp/inos_files.txt) 2>/dev/null | head -40
echo
echo "=== grep 'Sign' class/interface definitions ==="
grep -rn 'class .*Sign\|interface .*Sign\|class .*Cert\|interface .*Cert' $(cat /tmp/inos_files.txt) 2>/dev/null | head -40
