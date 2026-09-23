Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Sqlite3 {
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern int sqlite3_open(string filename, out IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern int sqlite3_prepare_v2(IntPtr db, string sql, int n, out IntPtr stmt, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern IntPtr sqlite3_errmsg(IntPtr db);
}
"@

$dbPath = "D:\idocNet\2017.A.iNOS.InBrand\Dev20\.svn\wc.db"
$db = [IntPtr]::Zero
$rc = [Sqlite3]::sqlite3_open($dbPath, [ref]$db)
Write-Output "open rc=$rc"
$stmt = [IntPtr]::Zero
$sql = "SELECT local_relpath, checksum FROM NODES WHERE local_relpath LIKE '%SignData.cs' OR local_relpath LIKE '%RSAUtil.cs'"
$rc = [Sqlite3]::sqlite3_prepare_v2($db, $sql, -1, [ref]$stmt, [IntPtr]::Zero)
Write-Output "prepare rc=$rc"
while ([Sqlite3]::sqlite3_step($stmt) -eq 100) {
    $p = [Sqlite3]::sqlite3_column_text($stmt, 0)
    $c = [Sqlite3]::sqlite3_column_text($stmt, 1)
    $ps = [Runtime.InteropServices.Marshal]::PtrToStringAnsi($p)
    $cs = [Runtime.InteropServices.Marshal]::PtrToStringAnsi($c)
    Write-Output "$ps => $cs"
}
[Sqlite3]::sqlite3_finalize($stmt) | Out-Null
[Sqlite3]::sqlite3_close($db) | Out-Null
