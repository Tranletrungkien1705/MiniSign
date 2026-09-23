#!/bin/bash
# grep -n a pattern in a specific pristine file
f="$1"; pat="$2"
grep -n "$pat" "$f" 2>/dev/null | head -40
