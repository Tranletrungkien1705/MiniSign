#!/bin/bash
# Find the pristine file containing the SignData class
ROOT=D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
grep -rl 'class SignData' "$ROOT" 2>/dev/null | head -5
echo "=== also search SignData in any file ==="
grep -rl 'SignData' "$ROOT" 2>/dev/null | head -10
