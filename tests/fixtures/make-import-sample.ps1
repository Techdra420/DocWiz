# Regenerates import-sample.docx — the committed fixture for `docwiz import-docx`
# end-to-end testing. Hand-crafted OOXML (a .docx is a zip of XML), so the fixture
# is deterministic and needs no Word installation. Mimics a migrated product manual:
# typed heading numbers, NOTE/WARNING paragraphs, a table, an embedded image.
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'import-sample.docx'

Add-Type -AssemblyName System.Drawing
$png = Join-Path $env:TEMP 'dw-fixture-stock.png'
$bmp = New-Object System.Drawing.Bitmap(200, 100)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::SteelBlue)
$g.DrawString('STOCK', (New-Object System.Drawing.Font('Arial', 24)), [System.Drawing.Brushes]::White, 30, 30)
$g.Dispose()
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

function P([string]$style, [string]$text) {
    $s = if ($style) { "<w:pPr><w:pStyle w:val=`"$style`"/></w:pPr>" } else { '' }
    "<w:p>$s<w:r><w:t xml:space=`"preserve`">$text</w:t></w:r></w:p>"
}
function Cell([string]$text) {
    "<w:tc><w:tcPr/><w:p><w:r><w:t>$text</w:t></w:r></w:p></w:tc>"
}

$drawing = @"
<w:p><w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0">
<wp:extent cx="1905000" cy="952500"/><wp:docPr id="1" name="dashboard.png" descr="Dashboard screenshot"/>
<a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
<a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
<pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
<pic:nvPicPr><pic:cNvPr id="1" name="dashboard.png" descr="Dashboard screenshot"/><pic:cNvPicPr/></pic:nvPicPr>
<pic:blipFill><a:blip r:embed="rId10"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1905000" cy="952500"/></a:xfrm>
<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
"@

$table = '<w:tbl><w:tblPr><w:tblBorders><w:top w:val="single"/><w:bottom w:val="single"/></w:tblBorders></w:tblPr>' +
    '<w:tblGrid><w:gridCol w:w="2400"/><w:gridCol w:w="4800"/></w:tblGrid>' +
    '<w:tr>' + (Cell 'Component') + (Cell 'Description') + '</w:tr>' +
    '<w:tr>' + (Cell 'Application server') + (Cell 'Web application and API') + '</w:tr>' +
    '<w:tr>' + (Cell 'Scanner station') + (Cell 'Handheld barcode scanner') + '</w:tr></w:tbl><w:p/>'

$document = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" ' +
    'xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" ' +
    'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body>' +
    (P '' '10042 Stockroom - User Guide') +
    (P '' 'Example Vendor') +
    (P 'Heading1' '1 Brief Introduction') +
    (P 'Heading2' '1.1 Modification') +
    (P '' 'Any configuration change must only be carried out by an administrator.') +
    (P '' 'NOTE: For contact information, please refer to the homepage of this user guide.') +
    (P 'Heading2' '1.2 Support responsibilities') +
    (P '' 'Support work must be carried out by trained personnel.') +
    (P 'Heading1' '2 System Design') +
    (P 'Heading2' '2.1 Dashboard') +
    (P '' 'The dashboard shows stock levels as in the screenshot below.') +
    $drawing +
    (P '' 'WARNING: Never share your sign-in badge with another user.') +
    $table +
    '</w:body></w:document>'

$styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">' +
    '<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/></w:style>' +
    '<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/></w:style></w:styles>'

$contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
    '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
    '<Default Extension="xml" ContentType="application/xml"/>' +
    '<Default Extension="png" ContentType="image/png"/>' +
    '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>' +
    '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>'

$rels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
    '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'

$docRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
    '<Relationship Id="rId10" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>' +
    '<Relationship Id="rId11" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>'

if (Test-Path $out) { Remove-Item $out -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($out, 'Create')
try {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    foreach ($entry in @(
        @('[Content_Types].xml', $contentTypes),
        @('_rels/.rels', $rels),
        @('word/document.xml', $document),
        @('word/styles.xml', $styles),
        @('word/_rels/document.xml.rels', $docRels))) {
        $e = $zip.CreateEntry($entry[0])
        $w = New-Object System.IO.StreamWriter($e.Open(), $utf8)
        $w.Write($entry[1]); $w.Dispose()
    }
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $png, 'word/media/image1.png')
}
finally { $zip.Dispose() }
Write-Host "Wrote $out"
