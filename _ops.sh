#!/bin/bash
# list all Invoice_Invoice_* operation names across pristine
grep -rhoE 'Invoice_Invoice_[A-Za-z0-9_]+' D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine 2>/dev/null | sort -u
