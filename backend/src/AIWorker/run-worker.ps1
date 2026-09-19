$ErrorActionPreference = 'Stop'
Get-Content "$PSScriptRoot\.env.worker" | ForEach-Object {
    if ($_ -match '^\s*([^#=]+)=(.*)$') {
        [Environment]::SetEnvironmentVariable($matches[1].Trim(), $matches[2].Trim(), 'Process')
    }
}
python -m uvicorn app:app --host 127.0.0.1 --port 8000 --app-dir "$PSScriptRoot"
