#!/usr/bin/env bash
# Rebuilds tools/.refs (the assemblies the compile checks build against) from scratch:
#   1. UnityEngine*.dll      UnityEngine.Modules 2021.3.33 reference assemblies (nuget.org, lib/net45)
#   2. UnityEngine.UI.dll    uGUI 2.0 from github.com/Unity-Technologies/uGUI, branch 6000.3,
#                            compiled against (1) + tools/refs-build/Unity6EngineShims.cs, with
#                            small patches for engine members newer than 2021.3
#   3. Unity.InputSystem.dll Input System 1.14.2 from github.com/Unity-Technologies/InputSystem
#                            (Packages/com.unity.inputsystem/InputSystem without the Editor folders)
# Needs: bash, curl, git, unzip, sed, dotnet (SDK 8+).
# Offline: point MODULES_NUPKG, UGUI_REPO, INPUTSYSTEM_REPO at a downloaded .nupkg / checkouts;
# REFS_OUT installs somewhere else than tools/.refs.
#   tools/fetch-refs.sh
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
out="${REFS_OUT:-$here/.refs}"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

DEFINES_COMMON="UNITY_2017_1_OR_NEWER;UNITY_2018_1_OR_NEWER;UNITY_2019_1_OR_NEWER;UNITY_2020_1_OR_NEWER;UNITY_2021_1_OR_NEWER;UNITY_2021_2_OR_NEWER;UNITY_2021_3_OR_NEWER"

echo "== 1/3 UnityEngine.Modules 2021.3.33"
nupkg=${MODULES_NUPKG:-}
if [ -z "$nupkg" ]; then
  nupkg="$work/modules.nupkg"
  curl -fsSL -o "$nupkg" "https://www.nuget.org/api/v2/package/UnityEngine.Modules/2021.3.33"
fi
mkdir -p "$work/modules"
unzip -q -o "$nupkg" 'lib/net45/*.dll' -d "$work/modules"
mods="$work/modules/lib/net45"
ls "$mods"/UnityEngine.CoreModule.dll >/dev/null

echo "== 2/3 uGUI (6000.3)"
ugui=${UGUI_REPO:-}
if [ -z "$ugui" ]; then
  ugui="$work/ugui-repo"
  git clone -q --depth 1 -b 6000.3 https://github.com/Unity-Technologies/uGUI "$ugui"
fi
src="$work/ugui-src"
cp -r "$ugui/com.unity.ugui/Runtime/UGUI" "$src"
# engine members that the 2021.3 reference assemblies do not have
sed -i 's/UnityEngineInternal\.DisplayInternal\.IsASecondaryDisplayIndex(displayIndex)/displayIndex > 0/' "$src/UI/Core/GraphicRaycaster.cs"
sed -i -e 's/(value\.GetSecondaryTextureCount() == 0)/true/' \
       -e 's/if (array1\[i\] != array2\[i\])/if (!array1[i].Equals(array2[i]))/' \
       -e 's/sprite != null ? sprite\.GetSecondaryTextureCount() : 0/0/' \
       -e '/sprite\.GetSecondaryTextures(newSecondaryTextures);/d' \
       -e '/renderer\.SetSecondaryTextureCount(/d' \
       -e '/renderer\.SetSecondaryTexture(i, /d' "$src/UI/Core/Image.cs"
sed -i '/rectTransform\.sendChildDimensionsChange = /d' "$src/UI/Core/Layout/LayoutGroup.cs"
sed -i '/^\s*MarkDirty();\s*$/d' "$src/UI/Core/Slider.cs"
if grep -q 'GetSecondaryTextureCount\|\.GetSecondaryTextures(\|renderer\.SetSecondaryTexture\|IsASecondaryDisplayIndex\|sendChildDimensionsChange' \
     "$src/UI/Core/GraphicRaycaster.cs" "$src/UI/Core/Image.cs" "$src/UI/Core/Layout/LayoutGroup.cs"; then
  echo "uGUI patches did not apply (the 6000.3 branch changed?)"; exit 1
fi
mkdir -p "$work/ugui"
cat > "$work/ugui/ugui.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>UnityEngine.UI</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Nullable>disable</Nullable>
    <LangVersion>9.0</LangVersion>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <NoWarn>CS0618;CS0612;CS0649;CS0414;CS0169;CS0067</NoWarn>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DefineConstants>$DEFINES_COMMON;PACKAGE_PHYSICS;PACKAGE_PHYSICS2D;PACKAGE_ANIMATION</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$src/**/*.cs" />
    <Compile Include="$here/refs-build/Unity6EngineShims.cs" />
    <Reference Include="$mods/*.dll" />
  </ItemGroup>
</Project>
EOF
dotnet build "$work/ugui/ugui.csproj" -nologo -v q -c Release -o "$work/ugui/out"

echo "== 3/3 Input System 1.14.2"
is=${INPUTSYSTEM_REPO:-}
if [ -z "$is" ]; then
  is="$work/inputsystem-repo"
  git clone -q --depth 1 -b 1.14.2 https://github.com/Unity-Technologies/InputSystem "$is"
fi
isrc="$is/Packages/com.unity.inputsystem/InputSystem"
mkdir -p "$work/inputsystem"
# the runtime code names this namespace; its classes live in the excluded Editor folders
echo 'namespace UnityEngine.InputSystem.Editor { internal static class CompileCheckShim {} }' > "$work/inputsystem/EditorNamespace.cs"
cat > "$work/inputsystem/inputsystem.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>Unity.InputSystem</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <Nullable>disable</Nullable>
    <LangVersion>9.0</LangVersion>
    <NoWarn>CS0618;CS0612;CS0649;CS0414;CS0169;CS0067;CS0108;CS0114;CS1998;CS0162;CS0219</NoWarn>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DefineConstants>$DEFINES_COMMON;UNITY_2019_4_OR_NEWER;UNITY_2020_2_OR_NEWER;UNITY_2020_3_OR_NEWER;ENABLE_INPUT_SYSTEM;UNITY_INPUT_SYSTEM_ENABLE_UI;PACKAGE_DOCS_GENERATION_OFF</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$isrc/**/*.cs" Exclude="$isrc/**/Editor/**/*.cs" />
    <Compile Include="$work/inputsystem/EditorNamespace.cs" />
    <Reference Include="$mods/*.dll" />
    <Reference Include="$work/ugui/out/UnityEngine.UI.dll" />
  </ItemGroup>
</Project>
EOF
dotnet build "$work/inputsystem/inputsystem.csproj" -nologo -v q -c Release -o "$work/inputsystem/out"

echo "== install into $out"
rm -rf "$out"
mkdir -p "$out"
cp "$mods"/*.dll "$out/"
cp "$work/ugui/out/UnityEngine.UI.dll" "$work/inputsystem/out/Unity.InputSystem.dll" "$out/"
echo "$(ls "$out" | wc -l) assemblies in $out"
