# FireHorse
Proyecto opensource que facilita la creación de webscrapper en .NET. Está construido sobre HTMLAgilityPack usando el patrón productor/consumidor, en el cual se puede establecer la cantidad máxima de conexiones simultáneas para no saturar el servidor web.

## 1.- Como funciona
Fire Horse es una clase estática, que implementa N ConcurrentQueue por cada dominio al cual se realizarán consultas; y se implementan tantos hilos como dominios existan, los cuales implementan el patrón Productor/Consumidor

## 2.- Instalación
Este complemento puede ser instalado vía Nuget Install-Package FireHorse

## 3.- Requisitos
Requiere contar con framework 4.5.2 o superior

## 4.- Uso
Primero, se deben definir los métodos o eventos que serán invocados al procesar el elemento. Actualmente se soportan tres eventos: OnDequeue, OnDataArrived y OnExceptio; de los cuales OnDequeue y OnException son opcionales, mientras que OnDataArrived es obligatorio.

#### 4.1- OnDequeue
Es invocado cada vez que un elemento es removido de la cola para ser procesado. Un mismo elemento puede ser removido de la cola varias veces, dado a las políticas de reintento que se implementan en el proceso. La firma es la siguiente:
```C#
private void OnDequeue(string url, IDictionary<string, string> optionalArguments)
{
  //Implementar lógica.
}
```

Se retorna la URL que será leída, así como una lista de clave-valor opcionales, útiles para personalizar el proceso de extracción.

#### 4.2- OnException
Es invocado cuando se produce un error y la política de reintentos establecidas fue superada. Por ejemplo, si se establece una política de tres reintentos, los primeros tres errores no gatillarán este evento; recién el cuarto error será notificado. La firma es la siguiente
```C#
private static void OnException(string url, IDictionary<string, string> optionalArguments, Exception ex)
{

}
```

#### 4.3.- OnDataArrived
Invocado cada vez que se retorna satisfactoriamente la información desde el servidor. La firma es la siguiente:

```C#
private static void OnDataArrived(string url, IDictionary<string, string> optionalArguments, HtmlDocument htmlDocument)
{

}
```

#### 4.4.- Agregar un nuevo elemento a la cola
Para agregar un nuevo elemento a la cola, se debe instanciar un nuevo ScraperData, el cual contiene información de la url que se analizará, así como punteros a los eventos mencionado en los puntos anteriores. Además, contiene un diccionario clave-valor, el cual es útil para agrupar distintos trozos de información que conceptualmente pertenecen a uno solo. 

```C#
var item = new ScraperData();
item.Url = url;
item.OnDequeue = OnDequeue;
item.OnDataArrived = OnDataArrived;
item.OnThrownException = OnException;
FireHorseManager.Enqueue(item);
```

#### 4.5.- Esperar a que el proceso concluya
*Opcion A: While con sleep*
Se puede implementar un while con sleep, de la siguiente manera

```C#
while (!FireHorseManager.IsEnded && !FireHorseManager.IsActive)
{
    Thread.Sleep(2000);                
}
```
*Opción B: Suscribirse al evento EndProcess*
Se puede suscribirse al evento `FireHorseManager.SubscribeToEndProcess`

```C#
//Crear un nuevo AutoResetEvent el cual esperará a que se reciba el evento
private static AutoResetEvent _waitHandle = new AutoResetEvent(false);

function getData()
{
  //En algún lugar del código, suscribirse al evento
  var subscriptionKey = FireHorseManager.SubscribeToEndProcess(OnFinish);

  //Do Something...

  //Esperar a que el evento se reciba
  _waitHandle.WaitOne();
}

//Crear método encargado de manejar el evento
private static void OnFinish()
{
    Console.WriteLine("Proceso finalizado.");
    _waitHandle.Set();
}
```

## 5.- Configuración
Además, se puede configurar la cantidad máxima de consultas realizadas a un mismo dominio. Por defecto, el valor es 40 y puede ser actualizado de la siguiente manera:

```C#
FireHorseManager.MaxRunningElementsByDomain = 5;
```

## 6.- Información sobre el proceso
FireHorse implementa algunas propiedades de solo lectura, que permiten conocer el estado del proceso

#### 6.1.- FireHorseManager.CurrentRunningSize
Retorna un entero que informa la cantidad de elementos que están siendo consultados al servidor.

```C#
int size = FireHorseManager.CurrentRunningSize;
```

#### 6.2.- FireHorseManager.CurrentQueueSize
Retorna un entero que informa la cantidad de elementos que existen en todas las colas

```C#
int size = FireHorseManager.CurrentQueueSize;
```

#### 6.3.- FireHorseManager.CurrentRunningSizeByDomain
Retorna un diccionario `(Dictionary<string, int>)` que contiene por cada dominio, la cantidad de elementos que están siendo consultados al servidor

```C#
foreach (var item in FireHorseManager.CurrentRunningSizeByDomain)
{
    Console.WriteLine("Dominio:{0}, Cantidad:{1}", item.Key, item.Value);
}
```

## 7.- Detener y Empezar el proceso
Por defecto, el sistema se iniciará de forma automática, y no se detendrá sin importar si hay elementos en la cola o no. Para detener el proceso manualmente, se puede utilizar el método `Stop()`. La llamada de este método puede tardar un par de segundos en completar, dado que internamente esperará a que los elementos que actualmente están en el estado de "*Running*", terminen su ejecución.

```C#
FireHorseManager.Stop();
```

Una vez que el proceso ha sido detenido manualmente, sin importar si existen elementos en la cola o no, quedará en ese estado hasta que manualmente sea iniciado con `Start()`. 

```C#
FireHorseManager.Start();
```
