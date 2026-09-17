# PDF -> docx bridge: drives the locally installed Microsoft Word, whose PDF
# reflow is the best fully-offline converter available. The output joins the
# normal `docwiz import-docx` pipeline (see tools/README.md).
#
# Usage:
#   .\tools\pdf2docx.ps1 -Path C:\Docs\folder    # every *.pdf in the folder
#   .\tools\pdf2docx.ps1 -Path C:\Docs\file.pdf  # a single file
param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$pdfs = if (Test-Path $Path -PathType Container) {
    Get-ChildItem -Path $Path -Filter *.pdf | ForEach-Object { $_.FullName }
} elseif (Test-Path $Path) {
    @((Resolve-Path $Path).Path)
} else {
    throw "Not found: $Path"
}
if ($pdfs.Count -eq 0) { Write-Host "No PDFs found in $Path"; exit 0 }

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0  # wdAlertsNone — never block on dialogs

try {
    foreach ($pdf in $pdfs) {
        $target = [System.IO.Path]::ChangeExtension($pdf, '.docx')
        if (Test-Path $target) {
            Write-Host "SKIP  $target already exists"
            continue
        }
        Write-Host "CONV  $pdf"
        # Word silently reflows the PDF on open (ConfirmConversions:=$false).
        $doc = $word.Documents.Open($pdf, $false, $true)  # confirm=no, readonly=yes
        $doc.SaveAs2($target, 16)                          # 16 = wdFormatXMLDocument (.docx)
        $doc.Close($false)
        Write-Host "  ->  $target"
    }
}
finally {
    $word.Quit()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)
}
Write-Host "Done. Review the .docx files in Word if the layout looks odd, then run docwiz import-docx."
