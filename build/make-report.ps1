# Yig'ish hisobotini BUILD-REPORT.md fayliga yozadi.
$ErrorActionPreference = 'Continue'
$lines = @()
$lines += "# AI Ustoz Pro — yig'ish hisoboti"
$lines += ""
$lines += "- Versiya: 0.2.0"
$lines += "- Yig'ilgan sana (UTC): $((Get-Date).ToUniversalTime().ToString('dd.MM.yyyy HH:mm'))"
$lines += "- Commit: $env:GITHUB_SHA"
$lines += "- Muhit: $([System.Environment]::OSVersion.VersionString), runner: $env:RUNNER_OS"
$lines += ""
$lines += "## Fayllar"
foreach ($f in @(Get-ChildItem publish\AiUstozPro.exe -ErrorAction SilentlyContinue) + @(Get-ChildItem installer\out\*.exe -ErrorAction SilentlyContinue)) {
  $mb = [math]::Round($f.Length / 1MB, 1)
  $sha = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
  $lines += "- $($f.Name) — $mb MB — SHA256 ``$sha``"
}
$lines += ""
$lines += "## Avtomatik testlar"
$trx = Get-ChildItem TestResults\*.trx -ErrorAction SilentlyContinue | Select-Object -First 1
if ($trx) {
  [xml]$x = Get-Content $trx.FullName
  $c = $x.TestRun.ResultSummary.Counters
  $lines += "- Jami: $($c.total), o'tdi: $($c.passed), yiqildi: $($c.failed), bajarilmadi: $($c.notExecuted)"
  $failed = $x.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -ne 'Passed' }
  foreach ($t in $failed) {
    $lines += "  - YIQILDI: $($t.testName)"
    $msg = ($t.Output.ErrorInfo.Message -replace "`r?`n", ' ')
    if ($msg.Length -gt 900) { $msg = $msg.Substring(0, 900) }
    $st = ($t.Output.ErrorInfo.StackTrace -split "`n" | Select-Object -First 3) -join ' | '
    Write-Host "::error title=Test yiqildi: $($t.testName)::$msg  @@ $st"
  }
  $lines += ""
  $lines += "<details><summary>Barcha testlar ro'yxati</summary>"
  $lines += ""
  foreach ($t in $x.TestRun.Results.UnitTestResult) { $lines += "- [$($t.outcome)] $($t.testName)" }
  $lines += ""
  $lines += "</details>"
} else {
  $lines += "- Test natijalari topilmadi (testlar bajarilmagan bo'lishi mumkin)."
}
$lines += ""
$lines += "## O'rnatish"
$lines += "1. ``AiUstozPro-Setup-0.2.0.exe`` ni ishga tushiring (administrator huquqi talab qilinmaydi)."
$lines += "2. Yoki ``AiUstozPro.exe`` ni istalgan papkadan to'g'ridan-to'g'ri ishga tushiring (portativ)."
$lines += "3. Birinchi ishga tushirishda administrator parolini yarating."
$lines += ""
$lines += "Batafsil: docs/FOYDALANUVCHI-QOLLANMASI.md va docs/CHEKLOVLAR.md"
$lines | Set-Content -Path BUILD-REPORT.md -Encoding utf8
Get-Content BUILD-REPORT.md

# Qisqa xulosani CI annotatsiyasi va sahifa xulosasi sifatida ham chiqaramiz.
$summaryLine = ($lines | Where-Object { $_ -like '- Jami:*' -or $_ -like '- AiUstozPro*' }) -join ' | '
Write-Host "::notice title=Yig'ish hisoboti::$summaryLine"
if ($env:GITHUB_STEP_SUMMARY) { $lines | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 }
