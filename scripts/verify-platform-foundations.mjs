import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const requireFile = relative => {
  const full = path.join(root, relative)
  if (!fs.existsSync(full)) throw new Error(`Missing ${relative}`)
  return read(relative)
}
const requireMatch = (content, pattern, message) => {
  if (!pattern.test(content)) throw new Error(message)
}

const taskModels = requireFile('src/PalOps.Web/Platform/Tasks/PlatformTaskModels.cs')
const taskCoordinator = requireFile('src/PalOps.Web/Platform/Tasks/PlatformTaskCoordinator.cs')
const taskRepository = requireFile('src/PalOps.Web/Platform/Tasks/PlatformTaskRepository.cs')
for (const token of ['Queued', 'Running', 'TimedOut', 'Interrupted', 'CancelAsync', 'RetryAsync', 'MaximumAttempts', 'ResourceKey']) {
  requireMatch(taskModels + taskCoordinator + taskRepository, new RegExp(`\\b${token}\\b`), `Task center contract missing ${token}`)
}
requireMatch(taskRepository, /\.tmp-|File\.Move/, 'Task repository must persist atomically')
requireMatch(taskCoordinator, /record\.Status == PlatformTaskStatus\.Completed\) return null/, 'Completed tasks must not be manually retried')
requireMatch(taskCoordinator, /status != PlatformTaskStatus\.Completed && _registrations\.ContainsKey/, 'Completed tasks must not expose retry capability')

const cache = requireFile('src/PalOps.Web/Platform/Caching/PlatformMemoryCache.cs')
for (const token of ['GetOrCreateAsync', 'RemoveByTag', '_factoryGates', 'GetSnapshot']) {
  requireMatch(cache, new RegExp(token), `Cache feature missing ${token}`)
}

const configuration = requireFile('src/PalOps.Web/AdvancedOperations/ConfigurationVersionService.cs')
requireMatch(configuration, /CaptureAutomaticAsync/, 'Automatic configuration snapshots missing')
requireMatch(configuration, /SanitizedSettings/, 'Sanitized settings snapshot missing')
requireMatch(configuration, /passwordConfigured/, 'Secret-presence metadata missing')
if (/summary\.PalworldPassword\b|summary\.RconPassword\b|summary\.PalDefenderToken\b/.test(configuration)) {
  throw new Error('Configuration snapshots must not persist plaintext secrets')
}

const health = requireFile('src/PalOps.Web/Health/SystemHealthService.cs')
for (const token of ['taskCenter', 'backgroundWorkers', 'platformCache', 'systemLogging', 'hostStorage', 'BuildDashboard']) {
  requireMatch(health, new RegExp(token), `Health dashboard missing ${token}`)
}

const logs = requireFile('src/PalOps.Web/Logging/SystemLogService.cs')
for (const token of ['GetSummaryAsync', 'ExportAsync', 'PurgeAsync', 'HasException', 'DroppedEntries']) {
  requireMatch(logs, new RegExp(token), `Log center missing ${token}`)
}
requireMatch(logs, /Dictionary<string, StringBuilder>/, 'Log writer batching missing')
requireMatch(logs, /Interlocked\.Add\(ref _writtenEntries, writtenEntries\)/, 'Log writer batch telemetry missing')

const readiness = requireFile('src/PalOps.Web/AdvancedOperations/AdvancedOperationsReadinessService.cs')
requireMatch(readiness, /setup-center/, 'First-run setup readiness missing')
requireMatch(requireFile('src/PalOps.Web/Endpoints/PalworldConfigurationEndpoints.cs'), /RemoveByTag\("readiness"\)/, 'Palworld configuration changes must invalidate readiness cache')

const responses = requireFile('src/PalOps.Web/Contracts/ApiContracts.cs') + requireFile('src/PalOps.Web/Contracts/V1Contracts.cs')
requireMatch(responses, /public bool Success => true/, 'Unified success response missing')
requireMatch(responses, /public bool Success => false/, 'Unified error response missing')
requireMatch(requireFile('src/PalOps.Web/Infrastructure/ApiResponseMetadataMiddleware.cs'), /X-Request-ID/, 'Response request-id metadata missing')

const workers = requireFile('src/PalOps.Web/Platform/Workers/BackgroundWorkerSupervisor.cs')
requireMatch(workers, /will be restarted/, 'Worker automatic restart missing')
requireMatch(workers, /RestartCount/, 'Worker restart telemetry missing')
requireMatch(workers, /DelayWithHeartbeatAsync/, 'Supervised long-delay heartbeat helper missing')
requireMatch(requireFile('src/PalOps.Web/SaveGames/SaveIndexMonitorService.cs'), /DelayWithHeartbeatAsync/, 'Save-index monitor may become falsely stale during long polling delays')
requireMatch(requireFile('src/PalOps.Web/Automation/AutomationSchedulerService.cs'), /DelayWithHeartbeatAsync/, 'Automation scheduler may become falsely stale during long polling delays')

const endpointDirectory = path.join(root, 'src/PalOps.Web/Endpoints')
for (const fileName of fs.readdirSync(endpointDirectory).filter(item => item.endsWith('.cs'))) {
  const relative = path.join('src/PalOps.Web/Endpoints', fileName)
  const source = read(relative)
  if (/AddEndpointFilter<CsrfValidationFilter>/.test(source)
      && !/using\s+PalOps\.Web\.Security\s*;/.test(source)
      && !/AddEndpointFilter<PalOps\.Web\.Security\.CsrfValidationFilter>/.test(source)) {
    throw new Error(`${relative} uses CsrfValidationFilter without importing PalOps.Web.Security`)
  }
}

const runtime = requireFile('src/PalOps.Web/ServerRuntime/PalServerRuntimeCoordinator.cs')
requireMatch(runtime, /platformTasks\.EnqueueAsync/, 'Server runtime operations are not attached to task center')
const backendSources = fs.readdirSync(path.join(root, 'src/PalOps.Web'), { recursive: true })
  .filter(item => String(item).endsWith('.cs'))
  .map(item => read(path.join('src/PalOps.Web', String(item))))
  .join('\n')
if (/Task\.Run\s*\(/.test(backendSources)) throw new Error('Raw Task.Run remains in backend source')

console.log('PalOps platform foundations backend verifier passed.')
