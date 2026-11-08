# Pc Information

Aplicativo desktop WPF para Windows 10/11, desenvolvido em C# e .NET 8.

## Requisitos
- Windows 10/11 64 bits
- .NET 8 SDK
- Internet na primeira compilação para restaurar os pacotes NuGet

## Executar
```powershell
dotnet restore
dotnet run
```

## Gerar executável
```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

O executável será gerado em `bin/Release/net8.0-windows/win-x64/publish/PcInformation.exe`.

## Recursos
- Uso de CPU e memória RAM
- Capacidade total, usada e livre dos discos fixos
- Identificação de CPU, GPU, placa-mãe e BIOS via WMI
- Sensores de temperatura, carga da GPU e VRAM quando suportados pelo hardware/driver
- Tráfego agregado de rede
- Atualização periódica do painel

## Limitações
Alguns sensores dependem de driver, permissões e suporte do fabricante. Quando uma leitura não está disponível, o programa informa isso em vez de inventar valores. Esta é uma versão inicial e deve ser compilada e validada em diferentes computadores antes de distribuição.