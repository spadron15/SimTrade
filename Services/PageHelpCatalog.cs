using SimTrade.ViewModels;

namespace SimTrade.Services;

public sealed record HelpSection(string Title, string Body);

public sealed record PageHelp(string Title, string Subtitle, IReadOnlyList<HelpSection> Sections);

public static class PageHelpCatalog
{
    public static PageHelp For(AppPage page) => page switch
    {
        AppPage.Portafolio => Portfolio,
        AppPage.Operar => Trade,
        AppPage.Rendimiento => Performance,
        AppPage.Analisis => Analysis,
        AppPage.Historial => History,
        AppPage.Cuentas => Accounts,
        AppPage.Acerca => About,
        _ => Portfolio
    };

    private static readonly PageHelp Portfolio = new(
        "Ayuda de portafolio",
        "Qué hace cada parte de esta pantalla.",
        [
            new("Para qué sirve",
                "Aquí ves la cuenta activa: efectivo, valor de las posiciones, patrimonio y resultado frente al capital invertido. Abajo está la tabla de activos con precios en tiempo casi real (Yahoo Finance)."),
            new("Tarjetas de resumen",
                "Efectivo disponible es el dinero libre en la cuenta (ya descuenta lo apartado en compras en cola). Valor de mercado suma lo que valen tus posiciones. Patrimonio es efectivo + mercado. Capital invertido es el inicial más aportaciones menos retiros. Resultado vs capital compara el patrimonio con ese capital."),
            new("Cola y apartado",
                "Si hay órdenes de compra pendientes fuera de horario, verás “Descontado en cola”. Ese monto ya salió del efectivo disponible hasta que la orden se ejecute o se cancele."),
            new("Tabla de posiciones",
                "Cada fila es un activo. Puedes ordenar por columnas, seleccionar una fila para gráficas y rangos, y usar Comprar o Vender para cargar el símbolo en Operar. El color de fondo de algunas columnas se configura en Columnas."),
            new("Botones de la barra",
                "Gráfica / intradía abren el precio del activo seleccionado. Rangos muestran mínimo, actual y máximo del día o de 52 semanas. Composición es el pastel del portafolio. Evolución grafica el patrimonio en el tiempo. Columnas deja mostrar, ocultar, renombrar, colorear y reordenar campos; la configuración se guarda."),
            new("Selección",
                "Al actualizar precios automáticamente se conserva el renglón seleccionado, para que el rango intradía y las gráficas no pierdan el activo.")
        ]);

    private static readonly PageHelp Trade = new(
        "Ayuda de operar",
        "Cómo armar y enviar una operación simulada.",
        [
            new("Para qué sirve",
                "Simulas compras y ventas en la cuenta activa. No se envían órdenes a un bróker real: todo queda en tu base local."),
            new("Símbolo y cotización",
                "Escribe un símbolo (por ejemplo AAPL o WALMEX.MX), elige una sugerencia y SimTrade trae precio, sesión y tipo de cambio. En Bolsa, fuera de horario regular las compras pueden ir a la cola; en Crypto se opera 24/7."),
            new("Compra o venta",
                "Elige el lado, el tipo de orden (mercado, limitada, stop o take profit) y si operas por cantidad o por monto. La vista previa muestra efectivo disponible, total estimado, comisión y efectivo después."),
            new("Cola pendiente",
                "Las órdenes en cola aparecen arriba. Puedes cancelarlas; en compras se libera el efectivo apartado. Las limitadas caducan según los días de vigencia que indiques."),
            new("Posiciones al vender",
                "Si cambias a Vender, puedes elegir una posición de la cuenta para cargar símbolo y cantidad disponible (restando lo ya apartado en ventas en cola).")
        ]);

    private static readonly PageHelp Performance = new(
        "Ayuda de rendimiento",
        "Comparación de todas tus cuentas.",
        [
            new("Para qué sirve",
                "Resume efectivo, mercado, patrimonio y resultado de cada portafolio en una sola vista."),
            new("Tarjetas consolidadas",
                "Arriba se suman las cuentas: efectivo total, valor de mercado, patrimonio y resultado agregado."),
            new("Tabla de cuentas",
                "Cada fila es un portafolio. El resultado usa capital invertido = inicial + aportaciones − retiros. Puedes activar una cuenta desde aquí si el flujo de la app lo permite en esa fila."),
            new("Comparar todas / Evolución",
                "Comparar todas abre la gráfica de patrimonio de varios portafolios a la vez. Evolución muestra solo la cuenta activa, con opción de benchmark.")
        ]);

    private static readonly PageHelp Analysis = new(
        "Ayuda de análisis",
        "Estadísticas de trading y ranking del día.",
        [
            new("Para qué sirve",
                "Mide cómo te ha ido con las operaciones cerradas de la cuenta activa y qué activos más aportan (o restan) al P&L del día."),
            new("Win rate y resultado realizado",
                "Win rate es el porcentaje de ventas con ganancia sobre las ventas cerradas. Resultado realizado suma el P&L de esas ventas. También ves rachas y otras métricas derivadas del historial."),
            new("Ranking del día",
                "Ordena posiciones por contribución al P&L intradía (variación del día × tamaño). Se actualiza con las cotizaciones. Seleccionar aquí no quita la selección del portafolio de forma destructiva: el ranking solo refleja la cuenta activa.")
        ]);

    private static readonly PageHelp History = new(
        "Ayuda de historial",
        "Movimientos de la cuenta activa.",
        [
            new("Para qué sirve",
                "Lista compras, ventas, aportaciones y retiros con filtros por símbolo, tipo y fechas."),
            new("Filtros y paginación",
                "Ajusta símbolo, tipo de operación y rango de fechas, luego aplica. La lista se pagina; usa Anterior/Siguiente si hay muchas filas."),
            new("Exportar y respaldo",
                "Exportar CSV guarda el historial filtrado. Respaldar BD copia el archivo SQLite completo (cuentas, posiciones, historial y cola)."),
            new("Notas de operación",
                "En operaciones de trading puedes revisar o editar notas asociadas cuando la app lo ofrece en la fila o flujo de detalle.")
        ]);

    private static readonly PageHelp Accounts = new(
        "Ayuda de cuentas",
        "Crear y administrar portafolios simulados.",
        [
            new("Para qué sirve",
                "Cada cuenta tiene su efectivo, comisiones, tipo de mercado (Bolsa o Crypto), icono, notas e historial independientes."),
            new("Lista e “En uso”",
                "La lista de la izquierda elige la cuenta a inspeccionar. “En uso” marca la cuenta activa del Portafolio y Operar. Usa Activar para operar con la seleccionada."),
            new("Nueva cuenta",
                "Nombre, icono, Bolsa/Crypto, efectivo inicial (por defecto 1,000,000 MXN), comisiones de compra/venta y una descripción opcional."),
            new("Aportar y retirar",
                "Ajustan el efectivo y el capital invertido usado en el rendimiento. Tras un retiro o aportación, SimTrade activa esa cuenta y muestra el efectivo resultante."),
            new("Editar o eliminar",
                "Puedes cambiar nombre, icono, notas y comisiones. El tipo de mercado solo se edita si no hay posiciones. Eliminar borra la cuenta y todo su historial (pide confirmación).")
        ]);

    private static readonly PageHelp About = new(
        "Ayuda de Acerca de SimTrade",
        "Versión, actualizaciones y contacto.",
        [
            new("Para qué sirve",
                "Muestra la versión instalada, cómo actualizar, datos de donación y el descargo de responsabilidad."),
            new("Actualizaciones",
                "Al iniciar, SimTrade consulta GitHub Releases. Si hay una versión nueva puedes descargar el instalador e instalarlo; la app se cierra un momento y vuelve a abrir. También puedes buscar actualizaciones desde esta pantalla."),
            new("Datos locales",
                "Todo se guarda en un SQLite en tu equipo. No hay servidor propio: los precios salen de Yahoo Finance mientras haya internet."),
            new("Aviso",
                "Es una herramienta educativa. No es asesoría de inversión ni un bróker real.")
        ]);
}
