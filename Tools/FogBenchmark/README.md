# Headless-замер обзора

После сборки `Game.Core.Tests.csproj` и `Game.Presentation.csproj` запустите из корня репозитория:

```powershell
dotnet run --project Tools/FogBenchmark/FogBenchmark.csproj -c Release
```

Бенчмарк запускается в Release, но использует готовые Debug-сборки игры из `Temp/bin/Debug`. Он создаёт карту 128×80 и 80 роботов, прогревает расчёт и измеряет три серии по 200 вызовов `FogOfWarSystem.Update`. Выделения считаются только на вызывающем потоке. Это не замер FPS, рендера или GC всей Unity-игры.
