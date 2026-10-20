# Compilar o TelaSync

Requisitos: Windows x64, Windows PowerShell e .NET Framework 4.8 com o compilador C# em `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.

Execute na raiz do repositório:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tela\build.ps1 -OutputDirectory .\output\TelaSync\local
```

Para o instalador completo:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tela\installer\build-installer.ps1 -OutputDirectory .\output\TelaSync\local
```

Para gerar o patch, extraia o ZIP portátil da versão de base em `releases\<patchFrom>`. A pasta deve conter `TelaSync.exe`, `TelaSync.exe.config`, `app.ico` e `LEIA-ME.txt` diretamente. A versão de base está em `Tela\product.json`.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tela\installer\build-patch.ps1 -OutputDirectory .\output\TelaSync\local -BaselineDirectory .\releases\0.1.2
```

O último comando gera também o instalador completo. Os manifestos em `Tela\installer` são regenerados durante a compilação e não são versionados.

## Verificação sem alterar os monitores

```powershell
$exe = (Resolve-Path .\output\TelaSync\local\TelaSync.exe).Path
$result = Join-Path (Resolve-Path .\output\TelaSync\local).Path 'self-test.json'
Start-Process -FilePath $exe -ArgumentList @('--self-test', ('"' + $result + '"')) -Wait -WindowStyle Hidden
Get-Content -LiteralPath $result
```

Os testes de lógica usam dados e controles simulados. Não execute modos de teste de hardware como parte de uma validação automática de publicação.

O script `Tela\tests\verify-update.ps1` testa instalação e atualização em pastas isoladas; leia seus parâmetros e requisitos antes de executar. Não use a instalação real do usuário como pasta de teste.

Os binários publicados em Releases são os builds locais originais, com SHA-256 próprio. Uma recompilação não necessariamente produz o mesmo hash.
