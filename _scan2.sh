#!/bin/bash
cd D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine || exit 1
echo "=== C# source files (namespace/using) ==="
grep -rli 'using System;\|namespace ' . 2>/dev/null | head -40
echo
echo "=== SQL files ==="
grep -rli 'CREATE TABLE\|CREATE PROCEDURE\|ALTER TABLE' . 2>/dev/null | head -40
