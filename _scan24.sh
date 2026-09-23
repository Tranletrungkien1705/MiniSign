#!/bin/bash
# Broad scan for signing-related source in both pristine stores
for ROOT in D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine; do
  echo "===== ROOT: $ROOT ====="
  grep -rli 'PKCS\|HSM\|USBToken\|SignData\|VerifyData\|SignedXml\|CmsSigner\|X509\|Certificate\|ChuKy\|ChungThu\|KySo\|Signature' "$ROOT" 2>/dev/null | while read f; do
    if ! head -c 2 "$f" | grep -q 'MZ'; then
      # only .cs-like files (contain 'namespace' or 'class' or 'using System')
      if head -c 4000 "$f" | grep -q 'namespace \|using System\|public class'; then
        echo "$f"
      fi
    fi
  done | head -60
done
