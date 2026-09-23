#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
echo "=== C# source files (namespace idn.iNOS) mentioning sign/cert ==="
for f in $(grep -rli 'namespace idn.iNOS' . 2>/dev/null); do
  if grep -qi 'sign\|cert\|chữ ký\|chu ky\|chung thu\|x509\|token' "$f" 2>/dev/null; then
    echo "$f"
  fi
done | head -60
