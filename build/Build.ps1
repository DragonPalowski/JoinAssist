param(
    [string]$SsmsRoot = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release',
    [string]$BuildDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\build'),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [string]$VssdkToolsPath = ''
)
$ErrorActionPreference = 'Stop'
$taskRepoRoot = Split-Path $PSScriptRoot -Parent
$taskSourceRoot = Join-Path $taskRepoRoot 'src\JoinAssist'
$taskTestsRoot = Join-Path $taskRepoRoot 'tests'
$taskPackagingRoot = Join-Path $taskRepoRoot 'packaging'
[xml]$taskManifestXml = Get-Content -Raw -LiteralPath (Join-Path $taskPackagingRoot 'source.extension.vsixmanifest')
$taskVersion = [string]$taskManifestXml.PackageManifest.Metadata.Identity.Version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'The VSIX version must be a numeric three- or four-part version.' }
$taskIde = Join-Path $SsmsRoot 'Common7\IDE'
$taskCompiler = Join-Path $SsmsRoot 'MSBuild\Current\Bin\Roslyn\csc.exe'
$taskFramework = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$taskEditor = Join-Path $taskIde 'CommonExtensions\Microsoft\Editor'
$taskScriptDom = Join-Path $taskIde 'Extensions\Application\Microsoft.SqlServer.TransactSql.ScriptDom.dll'
if (!(Test-Path -LiteralPath $taskCompiler)) { throw 'Compilatore non trovato. Specifica -SsmsRoot con la cartella Release di SSMS 22.' }
if (!(Test-Path -LiteralPath $taskFramework)) { throw 'Per compilare serve il targeting pack di .NET Framework 4.8.' }
if (!$VssdkToolsPath) { $VssdkToolsPath = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.vssdk.buildtools\18.9.820\tools\VSSDK' }
if (!(Test-Path -LiteralPath (Join-Path $VssdkToolsPath 'bin\VsixUtil.exe'))) { throw 'Servono Microsoft.VSSDK.BuildTools 18.9.820. Passa -VssdkToolsPath con la sua cartella tools\VSSDK.' }
$VssdkToolsPath = (Resolve-Path -LiteralPath $VssdkToolsPath).Path
New-Item -ItemType Directory -Path $BuildDirectory,$OutputDirectory -Force | Out-Null
$taskBuild = (Resolve-Path -LiteralPath $BuildDirectory).Path
$taskOut = (Resolve-Path -LiteralPath $OutputDirectory).Path
$taskFrameworkNames = @('mscorlib','System','System.Core','System.Data','System.Design','System.Xml','System.Net.Http','System.Web.Extensions','System.ComponentModel.Composition','System.Xaml','WindowsBase','PresentationCore','PresentationFramework','System.Windows.Forms')
$taskReferences = @($taskFrameworkNames | ForEach-Object { '/reference:' + (Join-Path $taskFramework ($_ + '.dll')) })
$taskReferences += '/reference:' + $taskScriptDom
$taskReferences += '/reference:' + (Join-Path $taskFramework 'Facades\netstandard.dll')
foreach ($taskAssembly in @('Microsoft.VisualStudio.CoreUtility','Microsoft.VisualStudio.Text.Data','Microsoft.VisualStudio.Text.Logic','Microsoft.VisualStudio.Text.UI','Microsoft.VisualStudio.Text.UI.Wpf','Microsoft.VisualStudio.Editor','Microsoft.VisualStudio.Language.Intellisense')) {
    $taskReferences += '/reference:' + (Join-Path $taskEditor ($taskAssembly + '.dll'))
}
foreach ($taskAssembly in @('Microsoft.VisualStudio.OLE.Interop','Microsoft.VisualStudio.TextManager.Interop','Microsoft.VisualStudio.Interop')) { $taskReferences += '/reference:' + (Join-Path $taskIde ("PublicAssemblies\" + $taskAssembly + '.dll')) }
$taskReferences += '/reference:' + (Join-Path $taskIde 'PublicAssemblies\Microsoft.VisualStudio.Shell.15.0.dll')
$taskReferences += '/reference:' + (Join-Path $taskIde 'PublicAssemblies\Microsoft.VisualStudio.Shell.Framework.dll')
$taskReferences += '/reference:' + (Join-Path $taskIde 'PublicAssemblies\Microsoft.VisualStudio.Threading.17.x\Microsoft.VisualStudio.Threading.dll')
$taskReferences += '/reference:' + (Join-Path $taskIde 'CommonExtensions\Platform\MefHosting\Microsoft.VisualStudio.ComponentModelHost.dll')
$taskMenu = Join-Path $taskBuild 'Menus.ctmenu'
& (Join-Path $VssdkToolsPath 'bin\VSCT.exe') (Join-Path $taskPackagingRoot 'Commands.vsct') $taskMenu /nologo ("/I" + (Join-Path $VssdkToolsPath 'inc'))
if ($LASTEXITCODE -ne 0) { throw 'Compilazione menu non riuscita.' }
$taskSources = @('Core.cs','SqlAnalysis.cs','ContextSql.cs','Catalog.cs','CompletionEngine.cs','Metadata.cs','SsmsConnection.cs','EditorExtension.cs','JoinDialog.cs','AutomaticTrigger.cs','AutomaticCompletion.cs','Updates.cs','UpdateDialog.cs','DeferredInstall.cs','DeferredScheduler.cs','NameMatching.cs','LocalTables.cs','Preferences.cs','MetadataCache.cs','JoinPaths.cs','Diagnostics.cs','InsertTemplates.cs','DmlCompletion.cs','AnalysisCache.cs','PreferencesDialog.cs','JoinAssistPackage.cs','UiTheme.cs','DiagnosticDialog.cs','SchemaDialogs.cs','MenuPlacement.cs','ExpressionTypes.cs','PredicateCompletion.cs','CompletionRanking.cs','SchemaTools.cs','DeletePlanner.cs','DeletePlanDialog.cs','IncrementalSql.cs') | ForEach-Object { Join-Path $taskSourceRoot $_ }
$taskVersionSource = Join-Path $taskBuild 'Version.cs'
[IO.File]::WriteAllText($taskVersionSource, "[assembly: System.Reflection.AssemblyVersion(""$taskVersion"")]`n[assembly: System.Reflection.AssemblyFileVersion(""$taskVersion"")]`n", [Text.UTF8Encoding]::new($false))
$taskSources += $taskVersionSource
$taskSources += Join-Path $taskSourceRoot 'CompletionHistory.cs'
$taskSources += Join-Path $taskSourceRoot 'TypingTemplates.cs'
$taskSources += Join-Path $taskSourceRoot 'PersonalRules.cs'
$taskSources += Join-Path $taskSourceRoot 'RulesEditor.cs'
$taskSources += @('WorkflowSql.cs','WorkflowMetadata.cs','QueryLibrary.cs','WorkflowDialog.cs','InlineFields.cs','RuleProbeDialog.cs') | ForEach-Object { Join-Path $taskSourceRoot $_ }
$taskWorker = Join-Path $taskBuild 'JoinAssist.UpdateInstaller.exe'
& $taskCompiler /nologo /noconfig /nostdlib+ /target:winexe /langversion:latest /deterministic+ /optimize+ /warnaserror+ "/out:$taskWorker" $taskReferences (Join-Path $taskSourceRoot 'DeferredInstall.cs') (Join-Path $taskSourceRoot 'UpdateInstaller.cs') $taskVersionSource
if ($LASTEXITCODE -ne 0) { throw 'Compilazione componente aggiornamento non riuscita.' }
$taskHarness = Join-Path $taskBuild 'DeferredWorkerHarness.exe'
& $taskCompiler /nologo /noconfig /nostdlib+ /target:exe /langversion:latest /deterministic+ /optimize+ /warnaserror+ "/out:$taskHarness" $taskReferences (Join-Path $taskSourceRoot 'DeferredInstall.cs') (Join-Path $taskTestsRoot 'DeferredWorkerHarness.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilazione test del processo di attesa non riuscita.' }
$taskMenuResources = [System.Resources.ResourceWriter]::new((Join-Path $taskBuild 'JoinAssist.Resources.resources'))
$taskMenuResources.Generate()
$taskMenuResources.Dispose()
& (Join-Path $SsmsRoot 'MSBuild\Current\Bin\MSBuild.exe') (Join-Path $taskPackagingRoot 'MenuResources.proj') /nologo /v:minimal ("/p:VssdkToolsPath="+$VssdkToolsPath) ("/p:PackageBuildPath="+$taskBuild)
if ($LASTEXITCODE -ne 0) { throw 'Creazione risorse menu non riuscita.' }
$taskDll = Join-Path $taskBuild 'JoinAssist.dll'
& $taskCompiler /nologo /noconfig /nostdlib+ /target:library /langversion:latest /deterministic+ /optimize+ /warnaserror+ "/out:$taskDll" ("/resource:"+$taskWorker+",JoinAssist.UpdateInstaller.exe") ("/resource:" + (Join-Path $taskBuild 'JoinAssist.Resources.resources') + ",JoinAssist.Resources.resources") $taskReferences $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Compilazione estensione non riuscita.' }
& (Join-Path $SsmsRoot 'MSBuild\Current\Bin\MSBuild.exe') (Join-Path $taskPackagingRoot 'MenuResources.proj') /t:Registration /nologo /v:minimal ("/p:VssdkToolsPath="+$VssdkToolsPath) ("/p:PackageBuildPath="+$taskBuild) ("/p:SsmsIde="+$taskIde)
if ($LASTEXITCODE -ne 0) { throw 'Registrazione pacchetto non generata.' }
$taskExpectedVersion = [version]$taskVersion
$taskExpectedVersion = [version]::new($taskExpectedVersion.Major, $taskExpectedVersion.Minor, $taskExpectedVersion.Build, [Math]::Max(0, $taskExpectedVersion.Revision))
if ([Reflection.AssemblyName]::GetAssemblyName($taskDll).Version -ne $taskExpectedVersion) { throw 'Assembly and VSIX versions differ.' }
$taskTests = Join-Path $taskBuild 'JoinAssist.Tests.exe'
$taskExtraTests = @((Join-Path $taskSourceRoot 'DeferredInstall.cs'),(Join-Path $taskTestsRoot 'DeferredInstallTests.cs'),(Join-Path $taskTestsRoot 'ProductivityTests.cs'),(Join-Path $taskSourceRoot 'IncrementalSql.cs'),(Join-Path $taskSourceRoot 'ExpressionTypes.cs'),(Join-Path $taskSourceRoot 'PredicateCompletion.cs'),(Join-Path $taskSourceRoot 'CompletionRanking.cs'),(Join-Path $taskSourceRoot 'SchemaTools.cs'),(Join-Path $taskSourceRoot 'DeletePlanner.cs'),(Join-Path $taskTestsRoot 'DeletePlannerTests.cs'),(Join-Path $taskTestsRoot 'AllFeaturesTests.cs'), (Join-Path $taskSourceRoot 'Preferences.cs'), (Join-Path $taskSourceRoot 'MetadataCache.cs'), (Join-Path $taskSourceRoot 'JoinPaths.cs'), (Join-Path $taskSourceRoot 'Diagnostics.cs'), (Join-Path $taskSourceRoot 'InsertTemplates.cs'),(Join-Path $taskSourceRoot 'DmlCompletion.cs'), (Join-Path $taskSourceRoot 'AnalysisCache.cs'), (Join-Path $taskTestsRoot 'CompletionEnhancementTests.cs'), (Join-Path $taskTestsRoot 'QueryContextRegressionTests.cs'), (Join-Path $taskSourceRoot 'NameMatching.cs'), (Join-Path $taskSourceRoot 'LocalTables.cs'), (Join-Path $taskSourceRoot 'ContextSql.cs'))
$taskExtraTests += (Join-Path $taskSourceRoot 'CompletionHistory.cs'),(Join-Path $taskTestsRoot 'SmartCompletionTests.cs')
$taskExtraTests += (Join-Path $taskTestsRoot 'WorkflowTests.cs')
$taskExtraTests += (Join-Path $taskSourceRoot 'WorkflowSql.cs'),(Join-Path $taskSourceRoot 'WorkflowMetadata.cs'),(Join-Path $taskSourceRoot 'QueryLibrary.cs')
$taskExtraTests += (Join-Path $taskSourceRoot 'TypingTemplates.cs'),(Join-Path $taskTestsRoot 'TypingTemplateTests.cs'),(Join-Path $taskSourceRoot 'PersonalRules.cs'),(Join-Path $taskTestsRoot 'PersonalRuleTests.cs')
& $taskCompiler /nologo /noconfig /nostdlib+ /target:exe /langversion:latest /deterministic+ /optimize+ /warnaserror+ "/out:$taskTests" $taskReferences $taskExtraTests (Join-Path $taskTestsRoot 'Tests.cs') (Join-Path $taskTestsRoot 'FakeDatabaseTests.cs') (Join-Path $taskTestsRoot 'UpdateTests.cs') (Join-Path $taskSourceRoot 'Core.cs') (Join-Path $taskSourceRoot 'SqlAnalysis.cs') (Join-Path $taskSourceRoot 'Catalog.cs') (Join-Path $taskSourceRoot 'CompletionEngine.cs') (Join-Path $taskSourceRoot 'Metadata.cs') (Join-Path $taskSourceRoot 'AutomaticTrigger.cs') (Join-Path $taskSourceRoot 'Updates.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilazione test non riuscita.' }
& $taskTests $taskIde
if ($LASTEXITCODE -ne 0) { throw 'Test non superati: pacchetto non generato.' }
$taskEditorTests = Join-Path $taskBuild 'JoinAssist.EditorSmokeTests.exe'
& $taskCompiler /nologo /noconfig /nostdlib+ /target:exe /langversion:latest /deterministic+ /optimize+ /warnaserror+ "/out:$taskEditorTests" "/reference:$taskDll" $taskReferences (Join-Path $taskTestsRoot 'NativeMenuTests.cs') (Join-Path $taskTestsRoot 'EditorSmokeTests.cs') (Join-Path $taskTestsRoot 'ControllerCommitTests.cs') (Join-Path $taskTestsRoot 'DeleteEditorTests.cs') (Join-Path $taskTestsRoot 'WorkflowWindowTests.cs') (Join-Path $taskTestsRoot 'WorkflowEditorTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilazione test editor non riuscita.' }
& $taskEditorTests $taskIde $taskDll
if ($LASTEXITCODE -ne 0) { throw 'Test del controller editor non superati: pacchetto non generato.' }
$taskAuthTests = Join-Path $taskBuild 'JoinAssist.AuthenticationTests.exe'
& $taskCompiler /nologo /noconfig /nostdlib+ /target:exe /langversion:latest /deterministic+ /optimize+ /warnaserror+ "/out:$taskAuthTests" $taskReferences (Join-Path $taskTestsRoot 'AuthenticationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilazione test autenticazione non riuscita.' }
& $taskAuthTests $taskIde $taskDll
if ($LASTEXITCODE -ne 0) { throw 'Test autenticazione non superati: pacchetto non generato.' }
$taskStagedPackage = Join-Path $taskBuild ("JoinAssist-" + $taskVersion + ".vsix")
$taskManifest = Join-Path $taskPackagingRoot 'source.extension.vsixmanifest'
& (Join-Path $SsmsRoot 'MSBuild\Current\Bin\MSBuild.exe') (Join-Path $taskPackagingRoot 'Package.proj') /nologo /v:minimal "/p:VssdkToolsPath=$VssdkToolsPath" "/p:JoinAssistAssembly=$taskDll" "/p:JoinAssistManifest=$taskManifest" "/p:PackageBuildPath=$taskBuild" "/p:JoinAssistPackage=$taskStagedPackage" "/p:JoinAssistLicense=$taskRepoRoot\LICENSE" "/p:JoinAssistRegistration=$taskBuild\JoinAssist.pkgdef"
if ($LASTEXITCODE -ne 0) { throw 'Creazione VSIX con gli strumenti Microsoft non riuscita.' }
$taskValidator = Join-Path $taskBuild 'ValidatePackage.exe'
& $taskCompiler /nologo /target:exe /deterministic+ /optimize+ /warnaserror+ "/out:$taskValidator" (Join-Path $PSScriptRoot 'ValidatePackage.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilazione validatore non riuscita.' }
$taskInstallerEngine = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\resources\app\ServiceHub\Services\Microsoft.VisualStudio.Setup.Service\VsixInstaller'
if (!(Test-Path -LiteralPath (Join-Path $taskInstallerEngine 'Microsoft.VisualStudio.ExtensionEngine.dll'))) { $taskInstallerEngine = $VssdkToolsPath }
& (Join-Path $PSScriptRoot 'Test-Packaging.ps1') -Package $taskStagedPackage -Validator $taskValidator -InstallerEngine $taskInstallerEngine -BuildDirectory $taskBuild
& $taskValidator $taskStagedPackage $taskInstallerEngine
if ($LASTEXITCODE -ne 0) { throw 'Il lettore Microsoft ha rifiutato il pacchetto. Nessun pacchetto pubblicato.' }
$taskPackage = Join-Path $taskOut ("JoinAssist-" + $taskVersion + ".vsix")
Copy-Item -LiteralPath $taskStagedPackage -Destination $taskPackage -Force
Write-Output "Creato: $taskPackage"
