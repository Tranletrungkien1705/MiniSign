#!/bin/bash
ROOT=D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
echo "########## Constants (37) lines 60-110 ##########"
sed -n '60,110p' "$ROOT/37/372ef534f25c510ebf582a12030f9028296861fd.svn-base"
echo ""
echo "########## search for Signature/SignData usage in text files ##########"
for f in $(grep -rl 'Signature\|SignData\|CertSerial\|wSigner' "$ROOT" 2>/dev/null); do
  if ! head -c 2 "$f" | grep -q 'MZ'; then
    if head -c 3000 "$f" | grep -q 'namespace \|using System'; then
      echo "===== $f ====="
      grep -n 'Signature\|SignData\|CertSerial\|wSigner\|Sign(' "$f" | head -15
    fi
  fi
done
