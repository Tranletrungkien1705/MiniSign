#!/bin/bash
BASE="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn"
echo "=== sign/cert related .cs paths ==="
grep -a -o '[A-Za-z0-9_./-]*\.cs' "$BASE/wc.db" | sort -u | grep -iE 'sign|cert|kyso|chungthu|token|hsm|usb|verify|serial|mst|tax' | grep -v 'HelpPage' | head -80
