dotnet build
cp RelicsFixDLL/bin/Debug/netstandard2.1/RelicsFixDLL.dll build/RelicsFixDLL.dll
cp RelicsFix/bin/Debug/netstandard2.1/RelicsFix.dll build/BepInEx/patchers/RelicsFix/RelicsFix.dll
cd build
zip -r ../RelicsFix.zip *
cd ..