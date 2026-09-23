#!/bin/bash
# Find Signer-related and invoice-signing source files in Dev20 pristine
ROOT=D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
echo "=== files with 'Signer' or 'KySo' or 'ChuKySo' or 'Sign' in content (text) ==="
grep -rli 'Signer\|KySo\|ChuKySo\|ChungThuSo\|SignInvoice\|SignXml\|SignData\|X509Certificate2\|CmsSigner\|SignedXml' "$ROOT" 2>/dev/null | while read f; do
  if ! head -c 2 "$f" | grep -q 'MZ'; then
    if head -c 6000 "$f" | grep -q 'namespace \|using System\|public class'; then
      echo "$f"
    fi
  fi
done | head -60
