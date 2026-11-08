# Pc Information

Aplicativo desktop WPF para Windows 10/11 64 bits, desenvolvido em C# e .NET 8. Exibe informações do computador e métricas de hardware/rede em um painel atualizado periodicamente.

## Recursos implementados

- Uso de CPU e temperatura quando o sensor é disponibilizado pelo hardware/driver.
- Memória RAM total, usada, livre e percentual de uso.
- Identificação da GPU e sensores de uso, temperatura e VRAM quando suportados.
- Capacidade total, usada e livre dos discos fixos.
- Identificação de CPU, GPU, placa-mãe e BIOS via WMI.
- Tráfego agregado de download e upload das interfaces de rede ativas.
- Nome do computador, versão do Windows e arquitetura.
- Atualização automática do painel a cada segundo.

## Requisitos para executar a partir do código

- Windows 10/11 64 bits.
- .NET 8 SDK.
- Acesso à internet na primeira compilação para restaurar os pacotes NuGet.

## Executar em desenvolvimento

```powershell
dotnet restore PcInformation.csproj
dotnet run --project PcInformation.csproj
```

## Gerar executável portátil

```powershell
dotnet publish PcInformation.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./artifacts/PcInformation-win-x64
```

## Gerar instalador

Com o Inno Setup 6 instalado, primeiro gere a publicação acima e depois execute:

```powershell
iscc installer/PcInformation.iss
```

O instalador será criado em `artifacts/PcInformation-Setup-win-x64.exe`.

## Compilação automática no GitHub

O workflow `.github/workflows/build-windows.yml` compila o aplicativo em um runner Windows sempre que há atualização na branch `main`, e também pode ser executado manualmente pela aba **Actions**. Quando a execução termina com sucesso, publica dois artefatos para download:
- `PcInformation-portable-win-x64`: aplicativo portátil.
- `PcInformation-Setup-win-x64`: instalador do Windows.

A compilação automatizada valida restauração, compilação e publicação. Ela não substitui testes em computadores físicos com diferentes CPUs, GPUs e drivers.

## Observações e limitações

- Temperatura, uso da GPU e VRAM dependem do suporte do fabricante, driver e permissões. O programa não inventa valores quando o sensor não existe.
- A taxa de rede é calculada a partir dos contadores das interfaces ativas e pode incluir tráfego local.
- A leitura de hardware é feita localmente; não há envio de métricas para um servidor.
- Esta versão deve ser validada em diferentes computadores antes de distribuição ampla.
