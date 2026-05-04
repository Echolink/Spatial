// See https://aka.ms/new-console-template for more information

using System.Numerics;
using Vtest;

SpatialService service = new SpatialService();

service.Init();
service.Tick();
service.Spawn(10018, new Vector3(0,0,7));
service.Tick();
service.Move(10018, new Vector3(8,0,5));


for(int i = 1;i<500;i++)
{
    Console.WriteLine($"Tick {i} : {service.GetPosition(10018)}");
    service.Tick();
}