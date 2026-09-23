#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
echo "=== SQL files mentioning Sign/Cert/ChuKy ==="
grep -rli 'Sign\|Cert\|ChuKy\|ChungThu\|KySo' $(grep -rli 'CREATE TABLE\|CREATE PROCEDURE\|ALTER TABLE' . 2>/dev/null) 2>/dev/null | head -30
echo
echo "=== all files (any) with 'Sign' in a table/column context ==="
grep -rln 'Sign_\|_Sign\|SignNo\|SignDate\|SignBy\|SignStatus\|SignType' . 2>/dev/null | head -30
