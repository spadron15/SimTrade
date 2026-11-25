# SimTrade

**Simulador de portafolios en pesos mexicanos (MXN)** para Windows.

Practica compras y ventas de acciones, ETFs y crypto **sin dinero real**. Los precios y el tipo de cambio USD/MXN se consultan en Yahoo Finance. Es una herramienta educativa: no ejecuta operaciones en el mercado.

![Plataforma](https://img.shields.io/badge/Windows-x64-0078D4?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet&logoColor=white)
![Licencia](https://img.shields.io/badge/licencia-MIT-green)

## Descarga

Instalador para Windows 10/11 (64 bits), autosuficiente (no necesitas instalar .NET por separado):

**[Releases → SimTrade-Setup](https://github.com/spadron15/SimTrade/releases/latest)**

1. Descarga `SimTrade-Setup-*.exe`
2. Ejecútalo e instala
3. Abre SimTrade desde el menú Inicio

## Qué puedes hacer

- **Cuentas** de Bolsa o Crypto con efectivo inicial, aportaciones y retiros
- **Operar** con órdenes de mercado, limitadas, stop / take profit y cola fuera de horario
- **Portafolio** con precios en MXN, costo promedio (MXN y USD), P&L, RSI, señales y columnas personalizables
- **Rendimiento** consolidado, evolución del patrimonio y comparación entre portafolios
- **Análisis**: ranking del día, estadísticas de trading, rangos intradía y 52 semanas
- **Historial** con filtros, paginación y exportación a CSV
- Datos locales en SQLite (en tu equipo); puedes respaldar la base desde la app

## Actualizaciones

Al iniciar, SimTrade consulta la última versión publicada en GitHub Releases. Si hay una más nueva, te pregunta si quieres **descargar e instalar**. También puedes hacerlo desde **Acerca de SimTrade → Buscar actualizaciones**.

El instalador cierra la app, reemplaza los archivos y la vuelve a abrir. Tus cuentas y la base SQLite local se conservan.

## Requisitos

- Windows 10 o 11, 64 bits
- Conexión a internet para cotizaciones (Yahoo Finance) y para comprobar actualizaciones

## Compilar desde el código

```powershell
# Ejecutar en modo desarrollo
dotnet run --project SimTrade.csproj

# Publicar + generar instalador (requiere Inno Setup 6)
.\Publicar.ps1
```

El instalador queda en `dist\SimTrade-Setup-<versión>.exe`.

## Aviso

SimTrade es solo para práctica y aprendizaje. No es asesoría de inversión ni un bróker. Toda decisión de inversión es responsabilidad de quien la toma.

## Licencia

MIT — ver [LICENSE](LICENSE).
