#!/bin/bash
grep -rln "Invoice_Invoice_Calc" D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
echo "----- AllocatedAndApprovedAndIssued -----"
grep -rln "Invoice_Invoice_AllocatedAndApprovedAndIssued" D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
echo "----- RefNo_ExistRefNo -----"
grep -rln "Invoice_Invoice_RefNo_ExistRefNo" D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
echo "----- InvoiceNoNotUnique -----"
grep -rln "Invoice_Invoice_InvoiceNoNotUnique" D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine
