# Build and scope

The supported CI build is Windows x64. GitHub Actions uses Visual Studio C++ via CMake and .NET 8 to produce NodeScope.exe and NodeScopeHook.dll.

This experimental release implements process selection, native module loading, and a simple overlay only. It does not yet implement Godot scene introspection or editing.