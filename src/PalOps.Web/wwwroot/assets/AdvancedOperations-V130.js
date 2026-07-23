import{C as computed,G as onMounted,H as onBeforeUnmount,P as h,gt as ref,mt as reactive,j as defineComponent,r as baseApi}from"./vue-i18n-CIubIkY4.js";
import{i as useRouter}from"./vue-router-DWmBZnwA.js";
import{t as ElMessage}from"./message-DViTk05D.js";
import{t as ElMessageBox}from"./message-box-8TTNmhcf.js";
import{t as authStore}from"./auth--4ppHZ6a.js";
import{n as formatBytes,t as formatDateTime}from"./format-CGzLUnlF.js";

let advancedCsrfToken='';
const csrfExemptPaths=new Set(['/api/auth/login','/api/auth/setup']);
async function refreshAdvancedCsrfToken(){const response=await request('/api/auth/csrf');advancedCsrfToken=response.token;return advancedCsrfToken}
async function request(path,init={}){const headers=new Headers(init.headers);const method=(init.method??'GET').toUpperCase();const hasBody=init.body!==undefined&&init.body!==null;if(hasBody&&!(init.body instanceof FormData)&&!headers.has('Content-Type'))headers.set('Content-Type','application/json');if(!['GET','HEAD','OPTIONS'].includes(method)&&!csrfExemptPaths.has(path)){if(!advancedCsrfToken)await refreshAdvancedCsrfToken();headers.set('X-CSRF-TOKEN',advancedCsrfToken)}const response=await fetch(path,{...init,headers,credentials:'same-origin'});if(!response.ok){let payload;try{payload=await response.json()}catch{}if(response.status===401)advancedCsrfToken='';const error=new Error(payload?.error?.message??('HTTP '+response.status));error.code=payload?.error?.code??('HTTP_'+response.status);error.status=response.status;throw error}if(response.status===204)return undefined;const contentType=response.headers.get('content-type')??'';return contentType.includes('application/json')?response.json():response.text()}
function jsonBody(value){return JSON.stringify(value)}
function withQuery(path,filters={},includePage=false){const params=new URLSearchParams();if(includePage){params.set('page',String(filters.page??1));params.set('pageSize',String(filters.pageSize??100))}for(const key of ['level','category','query','from','to','format'])if(filters[key])params.set(key,String(filters[key]));if(filters.hasException!==undefined)params.set('hasException',String(filters.hasException));return path+(params.size?'?'+params:'')}
async function download(path,init={}){const headers=new Headers(init.headers);const method=(init.method??'GET').toUpperCase();if(!['GET','HEAD','OPTIONS'].includes(method)){if(!advancedCsrfToken)await refreshAdvancedCsrfToken();headers.set('X-CSRF-TOKEN',advancedCsrfToken)}const response=await fetch(path,{...init,headers,credentials:'same-origin'});if(!response.ok){let payload;try{payload=await response.json()}catch{}throw new Error(payload?.error?.message??('HTTP '+response.status))}const disposition=response.headers.get('Content-Disposition');const encoded=/filename\*=UTF-8''([^;]+)/i.exec(disposition??'')?.[1];const plain=/filename="?([^";]+)"?/i.exec(disposition??'')?.[1];let filename=plain;if(encoded){try{filename=decodeURIComponent(encoded)}catch{filename=encoded}}return{blob:await response.blob(),filename}}
const api={...baseApi,
 advancedOperationsReadiness:module=>request('/api/v1/advanced-operations/readiness/'+encodeURIComponent(module)),
 diagnostics:()=>request('/api/v1/advanced-operations/diagnostics'),diagnosticsRun:()=>request('/api/v1/advanced-operations/diagnostics/run',{method:'POST'}),diagnosticsSupportBundle:()=>download('/api/v1/advanced-operations/diagnostics/support-bundle',{method:'POST'}),
 incidents:()=>request('/api/v1/advanced-operations/incidents'),createIncident:payload=>request('/api/v1/advanced-operations/incidents',{method:'POST',body:jsonBody(payload)}),incidentAction:(id,payload)=>request('/api/v1/advanced-operations/incidents/'+encodeURIComponent(id)+'/actions',{method:'POST',body:jsonBody(payload)}),saveIncidentRule:(id,payload)=>request('/api/v1/advanced-operations/incidents/rules'+(id?'/'+encodeURIComponent(id):''),{method:'PUT',body:jsonBody(payload)}),deleteIncidentRule:id=>request('/api/v1/advanced-operations/incidents/rules/'+encodeURIComponent(id),{method:'DELETE'}),evaluateIncidentHealth:()=>request('/api/v1/advanced-operations/incidents/evaluate-health',{method:'POST'}),
 playerInsights:()=>request('/api/v1/advanced-operations/player-insights'),updatePlayerInsightNote:(key,notes)=>request('/api/v1/advanced-operations/player-insights/'+encodeURIComponent(key)+'/note',{method:'PUT',body:jsonBody({notes})}),
 worldGovernance:()=>request('/api/v1/advanced-operations/world-governance'),reviewWorldGovernance:(id,status,note)=>request('/api/v1/advanced-operations/world-governance/'+encodeURIComponent(id)+'/review',{method:'PUT',body:jsonBody({status,note})}),
 disasterRecovery:()=>request('/api/v1/advanced-operations/disaster-recovery'),createDisasterRecoveryTarget:payload=>request('/api/v1/advanced-operations/disaster-recovery/targets',{method:'POST',body:jsonBody(payload)}),updateDisasterRecoveryTarget:(id,payload)=>request('/api/v1/advanced-operations/disaster-recovery/targets/'+encodeURIComponent(id),{method:'PUT',body:jsonBody(payload)}),validateDisasterRecoveryTarget:id=>request('/api/v1/advanced-operations/disaster-recovery/targets/'+encodeURIComponent(id)+'/validate',{method:'POST'}),runDisasterRecoveryDrill:(id,backupId)=>request('/api/v1/advanced-operations/disaster-recovery/targets/'+encodeURIComponent(id)+'/drills',{method:'POST',body:jsonBody({backupId})}),deleteDisasterRecoveryTarget:id=>request('/api/v1/advanced-operations/disaster-recovery/targets/'+encodeURIComponent(id),{method:'DELETE'}),
 updateCenter:(force=false)=>request('/api/v1/advanced-operations/updates?force='+force),updatePreflight:()=>request('/api/v1/advanced-operations/updates/preflight',{method:'POST'}),createUpdatePlan:payload=>request('/api/v1/advanced-operations/updates/plans',{method:'POST',body:jsonBody(payload)}),updateUpdatePlan:(id,payload)=>request('/api/v1/advanced-operations/updates/plans/'+encodeURIComponent(id),{method:'PUT',body:jsonBody(payload)}),approveUpdatePlan:(id,confirmation)=>request('/api/v1/advanced-operations/updates/plans/'+encodeURIComponent(id)+'/approve',{method:'POST',body:jsonBody({confirmation})}),deleteUpdatePlan:id=>request('/api/v1/advanced-operations/updates/plans/'+encodeURIComponent(id),{method:'DELETE'}),
 configurationVersions:()=>request('/api/v1/advanced-operations/configuration-versions'),createConfigurationVersion:(name,note='')=>request('/api/v1/advanced-operations/configuration-versions',{method:'POST',body:jsonBody({name,note})}),configurationVersionDiff:(fromId,toId)=>request('/api/v1/advanced-operations/configuration-versions/'+encodeURIComponent(fromId)+'/diff/'+encodeURIComponent(toId)),restoreConfigurationVersion:(id,confirmation,restart)=>request('/api/v1/advanced-operations/configuration-versions/'+encodeURIComponent(id)+'/restore',{method:'POST',body:jsonBody({confirmation,restart})}),deleteConfigurationVersion:id=>request('/api/v1/advanced-operations/configuration-versions/'+encodeURIComponent(id),{method:'DELETE'}),
 operationsPlaybooks:()=>request('/api/v1/advanced-operations/playbooks'),createOperationsPlaybook:payload=>request('/api/v1/advanced-operations/playbooks',{method:'POST',body:jsonBody(payload)}),updateOperationsPlaybook:(id,payload)=>request('/api/v1/advanced-operations/playbooks/'+encodeURIComponent(id),{method:'PUT',body:jsonBody(payload)}),runOperationsPlaybook:(id,confirmation='')=>request('/api/v1/advanced-operations/playbooks/'+encodeURIComponent(id)+'/run',{method:'POST',body:jsonBody({confirmation})}),deleteOperationsPlaybook:id=>request('/api/v1/advanced-operations/playbooks/'+encodeURIComponent(id),{method:'DELETE'}),
 securityCenter:()=>request('/api/v1/advanced-operations/security'),updateSecurityPolicy:payload=>request('/api/v1/advanced-operations/security/policy',{method:'PUT',body:jsonBody(payload)}),createApiToken:payload=>request('/api/v1/advanced-operations/security/tokens',{method:'POST',body:jsonBody(payload)}),revokeApiToken:(id,confirmation)=>request('/api/v1/advanced-operations/security/tokens/'+encodeURIComponent(id)+'/revoke',{method:'POST',body:jsonBody({confirmation})}),
 integrationCenter:()=>request('/api/v1/advanced-operations/integrations'),createIntegrationSubscription:payload=>request('/api/v1/advanced-operations/integrations/subscriptions',{method:'POST',body:jsonBody(payload)}),updateIntegrationSubscription:(id,payload)=>request('/api/v1/advanced-operations/integrations/subscriptions/'+encodeURIComponent(id),{method:'PUT',body:jsonBody(payload)}),deleteIntegrationSubscription:id=>request('/api/v1/advanced-operations/integrations/subscriptions/'+encodeURIComponent(id),{method:'DELETE'}),
 platformTasks:(limit=200)=>request('/api/v1/task-center?limit='+Math.max(1,Math.min(limit,500))),cancelPlatformTask:id=>request('/api/v1/task-center/'+encodeURIComponent(id)+'/cancel',{method:'POST'}),retryPlatformTask:id=>request('/api/v1/task-center/'+encodeURIComponent(id)+'/retry',{method:'POST'}),
 healthDashboard:()=>request('/api/v1/system/health/dashboard'),configurationReadiness:module=>request('/api/v1/system/configuration-readiness/'+encodeURIComponent(module)),
 systemLogs:(filters={})=>request(withQuery('/api/v1/system/logs',filters,true)),systemLogSummary:(filters={})=>request(withQuery('/api/v1/system/logs/summary',filters)),exportSystemLogs:(filters={},format='csv')=>download(withQuery('/api/v1/system/logs/export',{...filters,format})),clearSystemLogs:confirmation=>request('/api/v1/system/logs?confirmation='+encodeURIComponent(confirmation),{method:'DELETE'})};

if(!document.querySelector('link[data-palops-advanced-v130]')){const link=document.createElement('link');link.rel='stylesheet';link.href=new URL('./AdvancedOperations-V130.css',import.meta.url).href;link.dataset.palopsAdvancedV130='';document.head.appendChild(link)}

function advancedValue(input){return input&&typeof input==='object'&&input.__v_isRef===true?input.value:input}
function advancedArray(input){const value=advancedValue(input);return Array.isArray(value)?value:[]}
function advancedBool(input){return Boolean(advancedValue(input))}
function advancedText(value,fallback='—'){const resolved=advancedValue(value);if(resolved===undefined||resolved===null||resolved==='')return fallback;if(typeof resolved==='object'){if(typeof resolved.message==='string'&&resolved.message.trim())return resolved.message;if(typeof resolved.name==='string'&&resolved.name.trim())return resolved.name;try{return JSON.stringify(resolved)}catch{return fallback}}return String(resolved)}
function advancedDate(value){const resolved=advancedValue(value);if(!resolved)return '—';try{return formatDateTime(resolved)}catch{return String(resolved)}}
function advancedClass(...values){return values.flatMap(value=>Array.isArray(value)?value:[value]).filter(Boolean)}
function advancedButton(label,handler,options={}){return h('button',{type:'button',class:advancedClass('adv-btn',options.primary&&'is-primary',options.warning&&'is-warning',options.danger&&'is-danger',options.small&&'is-small'),disabled:advancedBool(options.disabled),title:options.title,onClick:event=>{event.preventDefault();if(!advancedBool(options.disabled))handler()}},label)}
function advancedTag(label,tone='info'){return h('span',{class:advancedClass('adv-tag','is-'+advancedText(tone,'info'))},advancedText(label))}
function advancedAlert(message,tone='info'){if(!message)return null;return h('div',{class:advancedClass('adv-alert','is-'+tone)},advancedText(message))}
function advancedHeader(title,description,actions=[]){return h('header',{class:'adv-header'},[h('div',{class:'adv-header-copy'},[h('h1',{},title),h('p',{},description)]),h('div',{class:'adv-header-actions'},actions.filter(Boolean))])}
function advancedMetrics(metrics){return h('div',{class:'advanced-metric-grid'},advancedArray(metrics).map((metric,index)=>h('article',{key:metric.label||index,class:advancedClass('advanced-metric','is-'+advancedText(metric.tone,'default'))},[h('span',{class:'advanced-metric__label'},advancedText(metric.label,'')),h('strong',{class:'advanced-metric__value'},advancedText(metric.value,'0')),metric.hint?h('small',{class:'advanced-metric__hint'},advancedText(metric.hint,'')):null]))) }
function advancedInputRef(model,options={}){const tag=options.multiline?'textarea':'input';return h(tag,{class:'adv-input',type:options.type||'text',rows:options.rows||undefined,value:advancedValue(model)??'',placeholder:options.placeholder||'',disabled:advancedBool(options.disabled),min:options.min,max:options.max,onInput:event=>{model.value=options.number?Number(event.target.value):event.target.value}})}
function advancedInputField(model,key,options={}){const tag=options.multiline?'textarea':'input';return h(tag,{class:'adv-input',type:options.type||'text',rows:options.rows||undefined,value:model[key]??'',placeholder:options.placeholder||'',disabled:advancedBool(options.disabled),min:options.min,max:options.max,onInput:event=>{model[key]=options.number?Number(event.target.value):event.target.value}})}
function advancedSelectRef(model,options,settings={}){return h('select',{class:'adv-select',value:advancedValue(model)??'',disabled:advancedBool(settings.disabled),onChange:event=>{model.value=event.target.value}},advancedArray(options).map(option=>{const normalized=typeof option==='string'?{value:option,label:option}:option;return h('option',{value:normalized.value,disabled:normalized.disabled},normalized.label)}))}
function advancedSelectField(model,key,options,settings={}){return h('select',{class:'adv-select',value:model[key]??'',disabled:advancedBool(settings.disabled),onChange:event=>{model[key]=event.target.value}},advancedArray(options).map(option=>{const normalized=typeof option==='string'?{value:option,label:option}:option;return h('option',{value:normalized.value,disabled:normalized.disabled},normalized.label)}))}
function advancedCheckField(model,key,label,options={}){return h('label',{class:advancedClass('adv-check',options.disabled&&'is-disabled')},[h('input',{type:'checkbox',checked:Boolean(model[key]),disabled:advancedBool(options.disabled),onChange:event=>{model[key]=event.target.checked}}),h('span',{},label)])}
function advancedCheckArray(model,key,value,label){const selected=Array.isArray(model[key])?model[key]:[];return h('label',{class:'adv-check'},[h('input',{type:'checkbox',checked:selected.includes(value),onChange:event=>{const current=Array.isArray(model[key])?[...model[key]]:[];model[key]=event.target.checked?[...new Set([...current,value])]:current.filter(item=>item!==value)}}),h('span',{},label)])}
function advancedArrayTextField(model,key,options={}){return h('textarea',{class:'adv-input',rows:options.rows||4,value:advancedArray(model[key]).join('\n'),placeholder:options.placeholder||'',onInput:event=>{model[key]=[...new Set(event.target.value.split(/[\n,]/).map(item=>item.trim()).filter(Boolean))]}})}
function advancedFormRow(label,control,hint=''){return h('label',{class:'adv-form-row'},[h('span',{class:'adv-form-label'},label),h('span',{class:'adv-form-control'},[control,hint?h('small',{class:'adv-form-hint'},hint):null])])}
function advancedModal(visibleRef,title,content,actions=[],options={}){if(!advancedBool(visibleRef))return null;return h('div',{class:'adv-modal-backdrop',onMousedown:event=>{if(event.target===event.currentTarget&&!options.locked)visibleRef.value=false}},[h('section',{class:advancedClass('adv-modal',options.wide&&'is-wide')},[h('header',{class:'adv-modal-header'},[h('h2',{},title),options.locked?null:advancedButton('关闭',()=>{visibleRef.value=false},{small:true})]),h('div',{class:'adv-modal-body'},Array.isArray(content)?content:[content]),h('footer',{class:'adv-modal-footer'},actions.filter(Boolean))])])}
function advancedTabs(model,items){return h('div',{class:'adv-tabs'},items.map(item=>advancedButton(item.label,()=>{model.value=item.value},{small:true,primary:advancedValue(model)===item.value})))}
function advancedTable(rows,columns){
  const data=advancedArray(rows)
  const header=h('thead',{},[h('tr',{},columns.map(column=>h('th',{class:column.class||''},column.label)))])
  const bodyRows=data.length?data.map((row,rowIndex)=>{
    const key=row.id||row.playerUid||row.userId||row.code||rowIndex
    const cells=columns.map(column=>{
      let value
      try{value=column.render?column.render(row,rowIndex):row[column.key]}
      catch(error){value='渲染失败：'+error.message}
      const children=Array.isArray(value)?value:[value===undefined||value===null?'—':value]
      return h('td',{class:column.class||'',title:typeof value==='string'?value:undefined},children)
    })
    return h('tr',{key},cells)
  }):[h('tr',{},[h('td',{class:'adv-empty',colspan:columns.length},'暂无数据')])]
  return h('div',{class:'adv-table-scroll'},[h('table',{class:'adv-table'},[header,h('tbody',{},bodyRows)])])
}

function advancedCard(children,className=''){return h('section',{class:advancedClass('content-card',className)},Array.isArray(children)?children:[children])}
function advancedFilters(children){return h('div',{class:'filters'},children.filter(Boolean))}
function advancedPage(title,description,actions,body){return h('section',{class:'advanced-page advanced-page-v130'},[advancedHeader(title,description,actions),...body.filter(Boolean)])}
function advancedActions(items){return h('div',{class:'adv-row-actions'},items.filter(Boolean))}
function advancedList(values,tone='info'){const items=advancedArray(values);return items.length?h('div',{class:'adv-tag-list'},items.map(value=>advancedTag(value,tone))):h('span',{class:'muted'},'无')}
function advancedKeyValue(label,value){return h('div',{class:'adv-key-value'},[h('strong',{},label),h('span',{},advancedText(value))])}
function advancedErrorMessage(cause){if(cause instanceof Error&&cause.message)return cause.message;if(typeof cause==='string'&&cause.trim())return cause;if(cause&&typeof cause==='object'){const nested=cause.error?.message;if(typeof nested==='string'&&nested.trim())return nested;if(typeof cause.message==='string'&&cause.message.trim())return cause.message}return '页面数据暂时不可用，请检查系统设置和后台日志。'}
function advancedFallbackReadiness(module,message){return{module,status:'setup-required',ready:false,title:'需要检查系统配置',description:'无法读取模块就绪状态。页面已进入安全降级模式，不会自动执行任何操作。',requiredSettings:['进入“系统设置”检查本地数据初始化、服务器连接、存档目录和备份目录。'],missingSettings:['确认后台已升级到 V1.3.1，并完成“系统设置”中的基础配置。'],recommendations:message?['错误详情：'+message]:[],generatedAt:new Date().toISOString()}}
const advancedReadinessRuntime=globalThis.__palopsReadinessRuntime||(globalThis.__palopsReadinessRuntime={cache:new Map(),inFlight:new Map()});
async function advancedResolveReadiness(module){const cached=advancedReadinessRuntime.cache.get(module);if(cached&&Date.now()-cached.checkedAt<60000)return cached.data;const existing=advancedReadinessRuntime.inFlight.get(module);if(existing)return existing;const request=api.configurationReadiness(module).then(response=>{const data=response.data;advancedReadinessRuntime.cache.set(module,{data,checkedAt:Date.now()});return data}).finally(()=>advancedReadinessRuntime.inFlight.delete(module));advancedReadinessRuntime.inFlight.set(module,request);return request}
function useAdvancedModuleReadiness(module){const readiness=ref(undefined);const pageError=ref('');async function refresh(){try{readiness.value=await advancedResolveReadiness(module);return readiness.value}catch(cause){const detail=advancedErrorMessage(cause);pageError.value=detail;readiness.value=advancedFallbackReadiness(module,detail);return readiness.value}}function captureError(cause){const detail=advancedErrorMessage(cause);pageError.value=detail;if(!readiness.value)readiness.value=advancedFallbackReadiness(module,detail)}function clearError(){pageError.value=''}return{readiness,pageError,refresh,captureError,clearError}}
function advancedNavigate(path){if(typeof window!=='undefined'&&window.location){if(typeof window.location.assign==='function')window.location.assign(path);else window.location.href=path}}
function advancedReadinessNotice(c){const state=c?.readinessState;if(!state)return null;const readiness=advancedValue(state.readiness);const error=advancedText(state.pageError,'');if(!error&&(!readiness||readiness.status==='ready'))return null;const missing=advancedArray(readiness?.missingSettings);const recommendations=advancedArray(readiness?.recommendations);const title=advancedText(readiness?.title,error?'页面数据暂时不可用':'建议完善系统配置');const description=advancedText(readiness?.description,'页面已进入安全降级模式，不会自动执行任何高风险操作。');return h('section',{class:advancedClass('adv-setup-notice',readiness?.status==='setup-required'&&'is-required')},[h('div',{class:'adv-setup-notice__copy'},[h('strong',{class:'adv-setup-notice__title'},title),h('p',{class:'adv-setup-notice__description'},description),missing.length?h('div',{class:'adv-setup-notice__section'},[h('b',{},'必须完成'),h('ol',{},missing.map(item=>h('li',{},advancedText(item))))]):null,recommendations.length?h('div',{class:'adv-setup-notice__section'},[h('b',{},'建议配置'),h('ul',{},recommendations.map(item=>h('li',{},advancedText(item))))]):null,error?h('p',{class:'adv-setup-notice__error'},'数据加载信息：'+error):null]),h('div',{class:'adv-setup-notice__actions'},[advancedButton('打开系统设置',()=>advancedNavigate('/system/settings'),{primary:true,small:true}),advancedButton('打开存档索引',()=>advancedNavigate('/system/save-index'),{small:true}),advancedButton('打开 Palworld 配置',()=>advancedNavigate('/server/configuration'),{small:true}),advancedButton('重新检测',()=>c.load(),{small:true,disabled:c.loading})])])}

function renderDiagnosticCenter(c){const report=advancedValue(c.report);const categories=[{value:'',label:'全部类别'},...advancedArray(c.categories).map(value=>({value,label:value}))];return advancedPage('诊断中心','集中检查服务连接、运行进程、存档、备份、磁盘与本地数据目录，并给出可执行的修复建议。',[advancedSelectRef(c.category,categories),advancedButton('刷新',()=>c.load(false),{disabled:c.loading}),advancedButton('一键体检',c.run,{primary:true,disabled:!advancedBool(c.canOperate)||advancedBool(c.operating)}),advancedButton('导出支持包',c.supportBundle,{disabled:!advancedBool(c.canAdminister)||advancedBool(c.operating)})],[advancedMetrics(c.metrics),report&&report.criticalCount?advancedAlert('检测到严重异常，请优先处理下表中的红色项目。','danger'):null,advancedCard(advancedTable(c.checks,[{label:'状态',render:row=>advancedTag(c.statusLabel(row.status),c.tagType(row.status))},{label:'类别',key:'category'},{label:'检查项',render:row=>[h('strong',{},row.name),h('div',{class:'muted'},row.code)]},{label:'耗时',render:row=>row.latencyMs==null?'—':row.latencyMs+' ms'},{label:'结果',key:'message'},{label:'修复建议',render:row=>h('span',{class:row.status==='critical'?'danger-text':''},row.remediation||'无需处理')},{label:'检查时间',render:row=>advancedDate(row.checkedAt)}]))])}

function renderIncidentCenter(c){const showIncidents=advancedValue(c.tab)==='incidents';const incidentTable=advancedTable(c.incidents,[{label:'级别',render:row=>advancedTag(c.label(row.severity),c.tagType(row.severity))},{label:'状态',render:row=>advancedTag(c.label(row.status),c.tagType(row.status))},{label:'故障',render:row=>[h('strong',{},row.title),h('div',{class:'muted'},row.description||row.source)]},{label:'次数',key:'occurrenceCount'},{label:'处理人',render:row=>row.assignee||'未分派'},{label:'最近观察',render:row=>advancedDate(row.lastObservedAt)},{label:'时间线',render:row=>advancedText(row.timeline?.at?.(-1)?.message)},{label:'操作',class:'adv-actions-cell',render:row=>advancedActions([advancedButton('确认',()=>c.action(row,'acknowledge'),{small:true,disabled:!advancedBool(c.canOperate)||row.status==='resolved'}),advancedButton('分派',()=>c.action(row,'assign'),{small:true,disabled:!advancedBool(c.canOperate)}),advancedButton('备注',()=>c.action(row,'comment'),{small:true,disabled:!advancedBool(c.canOperate)}),row.status!=='resolved'?advancedButton('恢复',()=>c.action(row,'resolve'),{small:true,primary:true,disabled:!advancedBool(c.canOperate)}):advancedButton('重开',()=>c.action(row,'reopen'),{small:true,warning:true,disabled:!advancedBool(c.canOperate)})])}]);const ruleTable=advancedTable(advancedValue(c.dashboard)?.rules??[],[{label:'规则',key:'name'},{label:'健康组件',key:'sourceCode'},{label:'触发状态',render:row=>advancedList(row.triggerStatuses)},{label:'级别',render:row=>advancedTag(c.label(row.severity),c.tagType(row.severity))},{label:'自动恢复',render:row=>row.autoResolve?'是':'否'},{label:'启用',render:row=>advancedTag(row.enabled?'启用':'停用',row.enabled?'success':'info')},{label:'更新人',key:'updatedBy'},{label:'更新时间',render:row=>advancedDate(row.updatedAt)},{label:'操作',render:row=>advancedActions([advancedButton('编辑',()=>c.openRule(row),{small:true,disabled:!advancedBool(c.canAdminister)}),advancedButton('删除',()=>c.deleteRule(row),{small:true,danger:true,disabled:!advancedBool(c.canAdminister)})])}]);const createModal=advancedModal(c.createVisible,'新建故障',[advancedFormRow('标题',advancedInputField(c.incidentForm,'title')),advancedFormRow('说明',advancedInputField(c.incidentForm,'description',{multiline:true,rows:4})),advancedFormRow('严重度',advancedSelectField(c.incidentForm,'severity',[{value:'critical',label:'严重'},{value:'high',label:'高'},{value:'medium',label:'中'},{value:'low',label:'低'}])),advancedFormRow('来源',advancedInputField(c.incidentForm,'source')),advancedFormRow('去重标识',advancedInputField(c.incidentForm,'fingerprint',{placeholder:'相同标识会聚合为同一故障'})),advancedFormRow('处理人',advancedInputField(c.incidentForm,'assignee'))],[advancedButton('取消',()=>{c.createVisible.value=false}),advancedButton('创建',c.createIncident,{primary:true,disabled:c.operating})]);const ruleModal=advancedModal(c.ruleVisible,advancedValue(c.editingRuleId)?'编辑告警规则':'新增告警规则',[advancedFormRow('名称',advancedInputField(c.ruleForm,'name')),advancedFormRow('健康组件',advancedInputField(c.ruleForm,'sourceCode',{placeholder:'例如 palworldRest、rcon、backups'})),advancedFormRow('触发状态',advancedArrayTextField(c.ruleForm,'triggerStatuses',{placeholder:'每行一个状态'})),advancedFormRow('严重度',advancedSelectField(c.ruleForm,'severity',[{value:'critical',label:'严重'},{value:'high',label:'高'},{value:'medium',label:'中'},{value:'low',label:'低'}])),advancedFormRow('自动恢复',advancedCheckField(c.ruleForm,'autoResolve','启用自动恢复')),advancedFormRow('启用',advancedCheckField(c.ruleForm,'enabled','启用规则'))],[advancedButton('取消',()=>{c.ruleVisible.value=false}),advancedButton('保存',c.saveRule,{primary:true,disabled:c.operating})]);return advancedPage('事件与故障中心','聚合重复告警，记录确认、分派、处置、恢复和复盘时间线，形成完整故障闭环。',[advancedButton('刷新',c.load,{disabled:c.loading}),advancedButton('评估健康规则',c.evaluate,{disabled:!advancedBool(c.canOperate)||advancedBool(c.operating)}),advancedButton('新建故障',c.openCreate,{primary:true,disabled:!advancedBool(c.canOperate)})],[advancedMetrics(c.metrics),advancedCard([advancedTabs(c.tab,[{label:'故障列表',value:'incidents'},{label:'告警规则',value:'rules'}]),showIncidents?advancedFilters([{value:'active',label:'活动故障'},{value:'open',label:'待处理'},{value:'acknowledged',label:'处理中'},{value:'resolved',label:'已恢复'},{value:'all',label:'全部'}].map(item=>advancedButton(item.label,()=>{c.statusFilter.value=item.value},{small:true,primary:advancedValue(c.statusFilter)===item.value}))):h('div',{class:'table-toolbar'},[advancedButton('新增规则',()=>c.openRule(),{primary:true,disabled:!advancedBool(c.canAdminister)})]),showIncidents?incidentTable:ruleTable]),createModal,ruleModal])}

function renderPlayerInsights(c){const dashboard=advancedValue(c.dashboard);const noteModal=advancedModal(c.noteVisible,'玩家备注 · '+advancedText(advancedValue(c.selected)?.name,''),advancedInputRef(c.noteText,{multiline:true,rows:8,placeholder:'记录人工核查、申诉、运营跟进或身份说明；清空后保存可删除备注。'}),[advancedButton('取消',()=>{c.noteVisible.value=false}),advancedButton('保存',c.saveNote,{primary:true,disabled:c.operating})]);return advancedPage('玩家洞察','综合在线状态、存档索引、纪律记录和身份变化，帮助管理员识别活跃、流失及需要人工核查的玩家。',[advancedButton('刷新',c.load,{disabled:c.loading})],[advancedAlert('风险分值仅用于辅助人工核查，不会触发自动踢出、封禁或白名单变更。','info'),advancedMetrics(c.metrics),advancedCard([advancedFilters([advancedInputRef(c.keyword,{placeholder:'搜索玩家、UID、公会、信号或备注'}),advancedSelectRef(c.risk,[{value:'',label:'全部风险'},{value:'high',label:'高风险'},{value:'medium',label:'中风险'},{value:'low',label:'低风险'},{value:'none',label:'无风险'}]),advancedSelectRef(c.activity,[{value:'all',label:'全部玩家'},{value:'online',label:'当前在线'},{value:'offline',label:'当前离线'},{value:'inactive',label:'长期未活动'}]),h('span',{class:'filter-count'},'显示 '+advancedArray(c.players).length+' / '+advancedText(dashboard?.totalPlayers,'0'))]),...advancedArray(dashboard?.warnings).map(value=>advancedAlert(value,'warning')),advancedTable(c.players,[{label:'在线',render:row=>advancedTag(row.online?'在线':'离线',row.online?'success':'info')},{label:'玩家',render:row=>[h('strong',{},row.name),h('div',{class:'muted'},row.userId||row.playerUid)]},{label:'等级',key:'level'},{label:'公会',render:row=>row.guildName||'未加入'},{label:'风险',render:row=>advancedTag(c.riskLabel(row.riskLevel)+' · '+row.riskScore,c.riskType(row.riskLevel))},{label:'纪律',render:row=>h('span',{class:row.violationCount>0?'danger':''},String(row.violationCount))},{label:'访问状态',render:row=>row.banned?advancedTag('已封禁','danger'):row.whitelisted?advancedTag('白名单','success'):'普通'},{label:'风险信号',render:row=>advancedList(row.signals,row.riskLevel==='high'?'danger':'warning')},{label:'最近活动',render:row=>row.online?'当前在线':advancedDate(row.lastSeenAt)},{label:'运营备注',render:row=>advancedText(row.notes)},{label:'操作',render:row=>advancedButton('备注',()=>c.editNote(row),{small:true,disabled:!advancedBool(c.canOperate)})}])]),noteModal])}

function renderWorldGovernance(c){const dashboard=advancedValue(c.dashboard);const reviewModal=advancedModal(c.reviewVisible,'治理复核 · '+advancedText(advancedValue(c.selected)?.title,''),[advancedAlert(advancedValue(c.selected)?.description||'','info'),advancedFormRow('复核结果',advancedSelectRef(c.selectedStatus,[{value:'reviewed',label:'已复核，无需处理'},{value:'action-required',label:'需要后续处理'},{value:'ignored',label:'忽略该候选项'},{value:'pending',label:'恢复待复核'}])),advancedFormRow('复核说明',advancedInputRef(c.reviewNote,{multiline:true,rows:6}))],[advancedButton('取消',()=>{c.reviewVisible.value=false}),advancedButton('保存复核',c.saveReview,{primary:true,disabled:c.operating})]);return advancedPage('世界分析与据点治理','只读分析公会、据点、归属关系和活动时间，生成需要人工确认的治理候选项。',[advancedButton('刷新',c.load,{disabled:c.loading})],[advancedAlert('本模块不会自动删除、移动或修改据点和公会数据；所有候选项仅供管理员复核。','info'),advancedMetrics(c.metrics),advancedCard([advancedFilters([advancedInputRef(c.keyword,{placeholder:'搜索标题、说明、公会 ID、据点 ID'}),advancedSelectRef(c.type,[{value:'',label:'全部类型'},...advancedArray(c.types).map(value=>({value,label:value}))]),advancedSelectRef(c.reviewStatus,[{value:'',label:'全部复核状态'},{value:'pending',label:'待复核'},{value:'reviewed',label:'已复核'},{value:'action-required',label:'需处理'},{value:'ignored',label:'已忽略'}]),h('span',{class:'filter-count'},'显示 '+advancedArray(c.candidates).length+' 项')]),...advancedArray(dashboard?.warnings).map(value=>advancedAlert(value,'warning')),advancedTable(c.candidates,[{label:'级别',render:row=>advancedTag(row.severity,c.severityType(row.severity))},{label:'类型',key:'candidateType'},{label:'候选项',render:row=>[h('strong',{},row.title),h('div',{class:'description'},row.description)]},{label:'公会 / 据点',render:row=>[h('code',{},row.guildId||'—'),h('div',{class:'muted'},row.baseId||'—')]},{label:'成员 / 工作帕鲁',render:row=>row.memberCount+' / '+row.workerCount},{label:'最近活动',render:row=>advancedDate(row.lastActivityAt)},{label:'复核状态',render:row=>advancedTag(row.reviewStatus,c.reviewType(row.reviewStatus))},{label:'复核说明',render:row=>advancedText(row.reviewNote)},{label:'操作',render:row=>advancedButton('复核',()=>c.openReview(row),{small:true,disabled:!advancedBool(c.canOperate)})}])]),reviewModal])}

function renderDisasterRecovery(c){const dashboard=advancedValue(c.dashboard);const targetMode=advancedValue(c.tab)==='targets';const targetTable=advancedTable(dashboard?.targets??[],[{label:'启用',render:row=>advancedTag(row.enabled?'启用':'停用',row.enabled?'success':'info')},{label:'名称',key:'name'},{label:'类型',key:'targetType'},{label:'目标位置',key:'endpoint'},{label:'凭据引用',render:row=>row.credentialReference||'无'},{label:'保留份数',key:'retentionCount'},{label:'最近验证',render:row=>[advancedTag(row.lastValidationStatus,c.tagType(row.lastValidationStatus)),h('div',{class:'muted'},row.lastValidationMessage||'—')]},{label:'验证时间',render:row=>row.lastValidatedAt?advancedDate(row.lastValidatedAt):'尚未验证'},{label:'操作',render:row=>advancedActions([advancedButton('编辑',()=>c.openEditor(row),{small:true,disabled:!advancedBool(c.canAdminister)}),advancedButton('验证',()=>c.validateTarget(row),{small:true,disabled:!advancedBool(c.canAdminister)||advancedBool(c.operating)}),advancedButton('演练',()=>c.drill(row),{small:true,warning:true,disabled:!advancedBool(c.isOwner)||!row.enabled||advancedBool(c.operating)}),advancedButton('删除',()=>c.remove(row),{small:true,danger:true,disabled:!advancedBool(c.isOwner)})])}]);const drillTable=advancedTable(dashboard?.recentDrills??[],[{label:'状态',render:row=>advancedTag(row.status,c.tagType(row.status))},{label:'灾备目标',key:'targetName'},{label:'备份文件',key:'backupFileName'},{label:'结果说明',key:'message'},{label:'执行人',key:'startedBy'},{label:'开始时间',render:row=>advancedDate(row.startedAt)},{label:'完成时间',render:row=>advancedDate(row.completedAt)}]);const modal=advancedModal(c.editorVisible,advancedValue(c.editingId)?'编辑灾备目标':'新增灾备目标',[advancedFormRow('名称',advancedInputField(c.form,'name')),advancedFormRow('目标类型',advancedSelectField(c.form,'targetType',[{value:'local',label:'本地目录'},{value:'unc',label:'Windows 网络共享'},{value:'webdav',label:'WebDAV'},{value:'s3-compatible',label:'S3 兼容存储'}])),advancedFormRow('目标位置',advancedInputField(c.form,'endpoint',{placeholder:'本地绝对路径、UNC 路径或 HTTPS 端点'})),advancedFormRow('凭据引用',advancedInputField(c.form,'credentialReference',{placeholder:'只保存部署环境中的凭据引用，不保存明文密码'})),advancedFormRow('保留份数',advancedInputField(c.form,'retentionCount',{type:'number',number:true,min:1,max:500})),advancedFormRow('启用',advancedCheckField(c.form,'enabled','启用灾备目标'))],[advancedButton('取消',()=>{c.editorVisible.value=false}),advancedButton('保存',c.save,{primary:true,disabled:c.operating})]);return advancedPage('灾备中心','管理本地、网络共享和远程灾备目标，执行不覆盖正式存档的隔离恢复演练。',[advancedButton('刷新',c.load,{disabled:c.loading}),advancedButton('新增灾备目标',()=>c.openEditor(),{primary:true,disabled:!advancedBool(c.canAdminister)})],[advancedAlert('灾备演练仅校验备份并复制到隔离目录，不会写入或覆盖正式 Palworld 存档。','info'),advancedMetrics(c.metrics),advancedCard([advancedTabs(c.tab,[{label:'灾备目标',value:'targets'},{label:'演练历史',value:'drills'}]),targetMode?targetTable:drillTable]),modal])}

function renderUpdateCenter(c){const dashboard=advancedValue(c.dashboard);const componentMode=advancedValue(c.tab)==='components';const blockers=dashboard&&!dashboard.preflight?.allowed?advancedCard([h('strong',{},'预检阻止原因'),h('ul',{},advancedArray(dashboard.preflight?.blockingReasons).map(item=>h('li',{},item)))],'blockers'):null;const componentTable=advancedTable(dashboard?.components??[],[{label:'组件',key:'component'},{label:'当前版本',render:row=>row.currentVersion||'未知'},{label:'最新版本',render:row=>row.latestVersion||'未获取'},{label:'状态',render:row=>advancedTag(row.status,c.tagType(row.status))},{label:'说明',key:'message'},{label:'检查时间',render:row=>advancedDate(row.checkedAt)}]);const planTable=advancedTable(dashboard?.plans??[],[{label:'状态',render:row=>advancedTag(row.status,c.tagType(row.status))},{label:'计划',key:'name'},{label:'组件',key:'targetComponent'},{label:'目标版本',key:'targetVersion'},{label:'兼容确认',render:row=>row.compatibilityAcknowledged?'已确认':'未确认'},{label:'最近结果',key:'lastMessage'},{label:'创建人',key:'createdBy'},{label:'更新时间',render:row=>advancedDate(row.updatedAt)},{label:'操作',render:row=>advancedActions([advancedButton('编辑',()=>c.openEditor(row),{small:true,disabled:!advancedBool(c.canAdminister)}),advancedButton('批准',()=>c.approve(row),{small:true,warning:true,disabled:!advancedBool(c.isOwner)}),advancedButton('删除',()=>c.remove(row),{small:true,danger:true,disabled:!advancedBool(c.canAdminister)})])}]);const modal=advancedModal(c.editorVisible,advancedValue(c.editingId)?'编辑更新计划':'新建更新计划',[advancedFormRow('计划名称',advancedInputField(c.form,'name')),advancedFormRow('目标组件',advancedInputField(c.form,'targetComponent',{placeholder:'例如 PalOps Web、PalDefender、PalServer 或插件名称'})),advancedFormRow('目标版本',advancedInputField(c.form,'targetVersion')),advancedFormRow('兼容性确认',advancedCheckField(c.form,'compatibilityAcknowledged','已核对游戏版本、插件依赖和回滚方案')),advancedFormRow('计划说明',advancedInputField(c.form,'note',{multiline:true,rows:5}))],[advancedButton('取消',()=>{c.editorVisible.value=false}),advancedButton('保存',c.save,{primary:true,disabled:c.operating})]);return advancedPage('游戏与组件更新中心','统一检查 PalOps、PalDefender、PalServer、插件与模组版本，保存更新计划并执行安全预检。',[advancedButton('刷新',()=>c.load(false),{disabled:c.loading}),advancedButton('强制检查版本',()=>c.load(true),{disabled:c.loading}),advancedButton('运行预检',c.preflight,{disabled:!advancedBool(c.canAdminister)||advancedBool(c.operating)}),advancedButton('新建更新计划',()=>c.openEditor(),{primary:true,disabled:!advancedBool(c.canAdminister)})],[advancedAlert('更新计划不会自动下载或替换 PalServer、插件或 PalOps 文件；批准后仍需在维护窗口执行实际更新。','info'),advancedMetrics(c.metrics),blockers,advancedCard([advancedTabs(c.tab,[{label:'组件版本',value:'components'},{label:'更新计划',value:'plans'}]),componentMode?componentTable:planTable]),modal])}

function renderConfigurationVersions(c){const dashboard=advancedValue(c.dashboard);const versions=advancedArray(dashboard?.versions);const versionOptions=[{value:'',label:'请选择版本'},...versions.map(item=>({value:item.id,label:item.name+' · '+advancedDate(item.createdAt)}))];const diff=advancedValue(c.diff);const compareCard=advancedCard(advancedFilters([advancedSelectField(c.compare,'fromId',versionOptions),h('span',{},'对比'),advancedSelectField(c.compare,'toId',versionOptions),advancedButton('比较差异',c.compareVersions,{disabled:c.operating}),h('span',{class:'current-path'},'当前文件：'+advancedText(dashboard?.currentPath,'未配置'))]),'compare-bar');const diffCard=diff?advancedCard([h('div',{class:'diff-title'},[h('strong',{},'差异结果'),advancedTag(diff.identical?'内容一致':advancedArray(diff.changedKeys).length+' 个键变化',diff.identical?'success':'warning')]),advancedList(diff.changedKeys,'warning'),advancedKeyValue('原启动参数',diff.launchArgumentsBefore||'—'),advancedKeyValue('目标启动参数',diff.launchArgumentsAfter||'—')],'diff-card'):null;const table=advancedTable(versions,[{label:'状态',render:row=>advancedTag(row.currentMatch?'当前匹配':'历史版本',row.currentMatch?'success':'info')},{label:'名称',key:'name'},{label:'说明',key:'note'},{label:'SHA-256',render:row=>h('code',{},String(row.sha256||'').slice(0,20))},{label:'大小',render:row=>Math.ceil((row.sizeBytes||0)/1024)+' KB'},{label:'创建人',key:'createdBy'},{label:'创建时间',render:row=>advancedDate(row.createdAt)},{label:'操作',render:row=>advancedActions([advancedButton('恢复',()=>c.restore(row),{small:true,warning:true,disabled:!advancedBool(c.isOwner)}),advancedButton('删除',()=>c.remove(row),{small:true,danger:true,disabled:!advancedBool(c.isOwner)})])}]);const modal=advancedModal(c.createVisible,'创建配置快照',[advancedFormRow('版本名称',advancedInputField(c.form,'name')),advancedFormRow('版本说明',advancedInputField(c.form,'note',{multiline:true,rows:5}))],[advancedButton('取消',()=>{c.createVisible.value=false}),advancedButton('创建',c.createSnapshot,{primary:true,disabled:!advancedBool(c.canAdminister)||advancedBool(c.operating)})]);return advancedPage('配置版本库','为 Palworld 配置与启动参数建立不可变快照、差异比较和可审计回滚。',[advancedButton('刷新',c.load,{disabled:c.loading}),advancedButton('创建快照',()=>{c.createVisible.value=true},{primary:true,disabled:!advancedBool(c.canAdminister)})],[advancedAlert('配置回滚仅对 Owner 开放，并要求精确确认词；系统不会无提示覆盖当前配置。','info'),advancedMetrics(c.metrics),compareCard,diffCard,advancedCard(table),modal])}

function renderOperationsPlaybooks(c){const dashboard=advancedValue(c.dashboard);const playbookMode=advancedValue(c.tab)==='playbooks';const table=advancedTable(dashboard?.playbooks??[],[{label:'启用',render:row=>advancedTag(row.enabled?'启用':'停用',row.enabled?'success':'info')},{label:'名称',key:'name'},{label:'说明',key:'description'},{label:'步骤',render:row=>advancedArray(row.steps).length},{label:'最近状态',render:row=>advancedTag(row.lastStatus,c.statusType(row.lastStatus))},{label:'最近结果',key:'lastMessage'},{label:'更新时间',render:row=>advancedDate(row.updatedAt)},{label:'操作',render:row=>advancedActions([advancedButton('编辑',()=>c.openEditor(row),{small:true,disabled:!advancedBool(c.canEdit)}),advancedButton('运行',()=>c.run(row),{small:true,primary:true,disabled:!advancedBool(c.isOwner)||!row.enabled}),advancedButton('删除',()=>c.remove(row),{small:true,danger:true,disabled:!advancedBool(c.isOwner)})])}]);const runs=advancedTable(dashboard?.recentRuns??[],[{label:'状态',render:row=>advancedTag(row.status,c.statusType(row.status))},{label:'剧本',key:'playbookName'},{label:'结果',key:'message'},{label:'步骤结果',render:row=>advancedList(advancedArray(row.steps).map(step=>step.order+'. '+step.action))},{label:'执行人',key:'startedBy'},{label:'开始时间',render:row=>advancedDate(row.startedAt)}]);const steps=advancedArray(c.form.steps).map((step,index)=>h('div',{class:'step-card'},[h('div',{class:'step-head'},[h('strong',{},'步骤 '+(index+1)),advancedActions([advancedButton('上移',()=>c.moveStep(index,-1),{small:true,disabled:index===0}),advancedButton('下移',()=>c.moveStep(index,1),{small:true,disabled:index===c.form.steps.length-1}),advancedButton('删除',()=>c.form.steps.splice(index,1),{small:true,danger:true})])]),advancedSelectField(step,'action',advancedArray(dashboard?.allowedActions)),advancedInputField(step,'parametersText',{multiline:true,rows:4,placeholder:'参数 JSON，例如 {"note":"自动备份","saveFirst":"true"}'}),advancedCheckField(step,'continueOnError','失败后继续下一步')]));const modal=advancedModal(c.editorVisible,advancedValue(c.editingId)?'编辑运维剧本':'新建运维剧本',[advancedFormRow('名称',advancedInputField(c.form,'name')),advancedFormRow('说明',advancedInputField(c.form,'description',{multiline:true,rows:3})),advancedFormRow('状态',advancedCheckField(c.form,'enabled','启用')),advancedFormRow('步骤',h('div',{class:'steps'},[...steps,advancedButton('添加步骤',c.addStep,{primary:true})]))],[advancedButton('取消',()=>{c.editorVisible.value=false}),advancedButton('保存',c.save,{primary:true,disabled:c.operating})],{wide:true});return advancedPage('自动化 2.0 / 运维剧本','使用白名单动作组合健康检查、备份、存档解析、通知和维护流程，不允许任意脚本执行。',[advancedButton('刷新',c.load,{disabled:c.loading}),advancedButton('新建剧本',()=>c.openEditor(),{primary:true,disabled:!advancedBool(c.canEdit)})],[advancedAlert('剧本只支持服务器预定义白名单动作；不存在 Shell、PowerShell、CMD 或任意进程执行入口。','warning'),advancedMetrics(c.metrics),advancedCard([advancedTabs(c.tab,[{label:'剧本',value:'playbooks'},{label:'运行历史',value:'runs'}]),playbookMode?table:runs]),modal])}

function renderSecurityCenter(c){const dashboard=advancedValue(c.dashboard);const tokenMode=advancedValue(c.tab)==='tokens';const tokens=advancedTable(dashboard?.tokens??[],[{label:'状态',render:row=>{const state=c.tokenStatus(row);return advancedTag(state.label,state.type)}},{label:'名称',key:'name'},{label:'前缀',render:row=>h('code',{},row.prefix)},{label:'作用域',render:row=>advancedList(row.scopes)},{label:'创建人',key:'createdBy'},{label:'到期时间',render:row=>advancedDate(row.expiresAt)},{label:'最近使用',render:row=>row.lastUsedAt?advancedDate(row.lastUsedAt):'从未使用'},{label:'操作',render:row=>advancedButton('吊销',()=>c.revoke(row),{small:true,danger:true,disabled:!advancedBool(c.isOwner)||Boolean(row.revokedAt)})}]);const observations=advancedTable(dashboard?.recentObservations??[],[{label:'结果',render:row=>advancedTag(row.outcome,row.outcome==='success'?'success':'danger')},{label:'类型',key:'observationType'},{label:'主体',key:'actor'},{label:'远程 IP',key:'remoteIp'},{label:'说明',key:'message'},{label:'时间',render:row=>advancedDate(row.occurredAt)}]);const policyModal=advancedModal(c.policyVisible,'安全策略',[advancedFormRow('允许 API 令牌',advancedCheckField(c.policyForm,'apiTokensEnabled','允许创建和使用 API 令牌')),advancedFormRow('令牌最长有效期',advancedInputField(c.policyForm,'maximumTokenDays',{type:'number',number:true,min:1,max:365}),'天'),advancedFormRow('最多活动令牌',advancedInputField(c.policyForm,'maximumActiveTokens',{type:'number',number:true,min:1,max:100})),advancedFormRow('高风险精确确认',advancedCheckField(c.policyForm,'requireHighRiskConfirmation','安全基线要求，不能关闭',{disabled:true})),advancedFormRow('审计令牌使用',advancedCheckField(c.policyForm,'auditTokenUse','记录令牌使用观察'))],[advancedButton('取消',()=>{c.policyVisible.value=false}),advancedButton('保存',c.savePolicy,{primary:true,disabled:c.operating})]);const tokenModal=advancedModal(c.tokenVisible,'创建 API 令牌',[advancedFormRow('令牌名称',advancedInputField(c.tokenForm,'name')),advancedFormRow('作用域',h('div',{class:'adv-check-list'},c.availableScopes.map(scope=>advancedCheckArray(c.tokenForm,'scopes',scope,scope)))),advancedFormRow('有效天数',advancedInputField(c.tokenForm,'expiresInDays',{type:'number',number:true,min:1,max:dashboard?.policy?.maximumTokenDays??365}))],[advancedButton('取消',()=>{c.tokenVisible.value=false}),advancedButton('创建',c.createToken,{primary:true,disabled:c.operating})]);const created=advancedValue(c.createdToken);const resultModal=advancedModal(c.tokenResultVisible,'API 令牌已创建',[advancedAlert('这是唯一一次显示完整令牌。请立即复制并保存到密码管理器。','success'),h('div',{class:'token-secret'},[h('code',{},advancedText(created?.plainText,'')),advancedButton('复制',c.copyToken,{primary:true})])],[advancedButton('我已安全保存',c.closeTokenResult,{primary:true})],{locked:true});return advancedPage('安全中心','集中管理外部 API 令牌、安全策略和令牌使用观察；令牌秘密只显示一次且服务端仅保存哈希。',[advancedButton('刷新',c.load,{disabled:c.loading}),advancedButton('安全策略',c.openPolicy,{disabled:!advancedBool(c.isOwner)}),advancedButton('创建 API 令牌',c.openToken,{primary:true,disabled:!advancedBool(c.isOwner)||!dashboard?.policy?.apiTokensEnabled})],[advancedAlert('令牌明文仅在创建成功后显示一次。关闭窗口前请安全保存；平台不会再次返回该秘密。','warning'),advancedMetrics(c.metrics),advancedCard([advancedTabs(c.tab,[{label:'API 令牌',value:'tokens'},{label:'安全观察',value:'observations'}]),tokenMode?tokens:observations]),policyModal,tokenModal,resultModal])}

function renderIntegrationCenter(c){const dashboard=advancedValue(c.dashboard);const subscriptionMode=advancedValue(c.tab)==='subscriptions';const subscriptions=advancedTable(dashboard?.subscriptions??[],[{label:'状态',render:row=>advancedTag(row.enabled?'启用':'停用',row.enabled?'success':'info')},{label:'名称',key:'name'},{label:'事件类型',render:row=>advancedList(row.eventTypes)},{label:'HTTPS 目标',key:'destination'},{label:'秘密引用',render:row=>row.signingSecretReference||'未配置'},{label:'最近状态',render:row=>advancedTag(row.lastStatus,c.statusType(row.lastStatus))},{label:'更新时间',render:row=>advancedDate(row.updatedAt)},{label:'操作',render:row=>advancedActions([advancedButton('编辑',()=>c.openEditor(row),{small:true,disabled:!advancedBool(c.canEdit)}),advancedButton('删除',()=>c.remove(row),{small:true,danger:true,disabled:!advancedBool(c.isOwner)})])}]);const events=advancedTable(dashboard?.recentEvents??[],[{label:'结果',render:row=>advancedTag(row.outcome,c.statusType(row.outcome))},{label:'事件类型',key:'eventType'},{label:'来源',key:'source'},{label:'幂等键',key:'idempotencyKey'},{label:'说明',key:'message'},{label:'接收时间',render:row=>advancedDate(row.receivedAt)}]);const modal=advancedModal(c.editorVisible,advancedValue(c.editingId)?'编辑集成订阅':'新建集成订阅',[advancedFormRow('订阅名称',advancedInputField(c.form,'name')),advancedFormRow('启用',advancedCheckField(c.form,'enabled','启用订阅')),advancedFormRow('事件类型',advancedInputField(c.form,'eventTypesText',{multiline:true,rows:5,placeholder:'每行一个事件类型，也可使用逗号分隔'})),advancedFormRow('HTTPS 目标',advancedInputField(c.form,'destination',{placeholder:'https://example.com/webhooks/palops'})),advancedFormRow('秘密引用备注',advancedInputField(c.form,'signingSecretReference',{placeholder:'仅登记外部秘密引用名称，不读取或保存明文秘密'}))],[advancedButton('取消',()=>{c.editorVisible.value=false}),advancedButton('保存',c.save,{primary:true,disabled:c.operating})]);return advancedPage('对外集成中心','管理受限事件订阅和外部 Bearer Token 接口；订阅会进入现有 Webhook 队列、重试、SSRF 防护和投递历史。',[advancedButton('刷新',c.load,{disabled:c.loading}),advancedButton('新建订阅',()=>c.openEditor(),{primary:true,disabled:!advancedBool(c.canEdit)})],[advancedAlert('订阅目标必须使用 HTTPS。系统不会提供任意 RCON 或 Shell 入站执行能力；外部事件类型必须以 external. 开头。','info'),advancedMetrics(c.metrics),advancedCard([advancedTabs(c.tab,[{label:'事件订阅',value:'subscriptions'},{label:'入站事件',value:'events'}]),subscriptionMode?subscriptions:events]),modal])}

function renderTaskCenter(c){const dashboard=advancedValue(c.dashboard);const statusOptions=[{value:'',label:'全部状态'},...['queued','running','completed','failed','cancelled','timedOut','interrupted'].map(value=>({value,label:c.statusLabel(value)}))];const metrics=[{label:'排队中',value:dashboard?.queued??0,tone:'warning'},{label:'执行中',value:dashboard?.running??0,tone:'info'},{label:'24 小时失败',value:dashboard?.failedLast24Hours??0,tone:(dashboard?.failedLast24Hours??0)>0?'danger':'default'},{label:'24 小时完成',value:dashboard?.completedLast24Hours??0,tone:'success'}];const rows=advancedArray(c.items);const table=advancedTable(rows,[{label:'状态',render:row=>advancedTag(c.statusLabel(row.status),c.statusType(row.status))},{label:'任务',render:row=>h('div',{},[h('strong',{},advancedText(row.name)),h('div',{class:'muted'},advancedText(row.type)+' · '+advancedText(row.id))])},{label:'进度',render:row=>h('div',{class:'adv-progress'},[h('progress',{max:100,value:Number(row.progress)||0}),h('span',{},(Number(row.progress)||0)+'%')])},{label:'阶段 / 消息',render:row=>h('div',{},[h('strong',{},advancedText(row.stage)),h('div',{class:'muted'},advancedText(row.message||row.errorDetail))])},{label:'尝试',render:row=>advancedText(row.attempt,'0')+'/'+advancedText(row.maximumAttempts,'0')},{label:'耗时',render:row=>c.duration(row)},{label:'创建时间',render:row=>advancedDate(row.createdAt)},{label:'操作',render:row=>advancedActions([row.canCancel?advancedButton('取消',()=>c.cancel(row),{small:true,warning:true,disabled:!advancedBool(c.canOperate)||advancedValue(c.operatingId)===row.id}):null,row.canRetry?advancedButton('重试',()=>c.retry(row),{small:true,primary:true,disabled:!advancedBool(c.canOperate)||advancedValue(c.operatingId)===row.id}):null])}]);return advancedPage('统一任务中心','集中查看后台任务的排队、进度、超时、取消、重试与执行历史；存档解析、维护和服务器控制已接入统一调度。',[advancedSelectRef(c.statusFilter,statusOptions),advancedButton('刷新',()=>c.load(false),{disabled:c.loading})],[advancedMetrics(metrics),advancedCard(table)])}

function renderHealthCenter(c){const dashboard=advancedValue(c.dashboard);const metrics=[{label:'健康评分',value:(dashboard?.score??0)+'%',hint:c.statusLabel(dashboard?.overallStatus),tone:(dashboard?.score??0)>=90?'success':(dashboard?.score??0)>=70?'warning':'danger'},{label:'后台工作器',value:(dashboard?.workers?.running??0)+' / '+(dashboard?.workers?.total??0),hint:'失败 '+(dashboard?.workers?.failed??0)+' · 过期 '+(dashboard?.workers?.stale??0)+' · 重启 '+(dashboard?.workers?.restarts??0)},{label:'缓存命中率',value:Math.round(dashboard?.cache?.hitRate??0)+'%',hint:'条目 '+(dashboard?.cache?.entries??0)+' · 淘汰 '+(dashboard?.cache?.evictions??0)},{label:'运行任务',value:dashboard?.tasks?.running??0,hint:'排队 '+(dashboard?.tasks?.queued??0)+' · 24小时失败 '+(dashboard?.tasks?.failedLast24Hours??0)},{label:'磁盘可用',value:Number(dashboard?.host?.diskFreePercent??0).toFixed(1)+'%',hint:formatBytes(dashboard?.host?.diskFreeBytes)+' / '+formatBytes(dashboard?.host?.diskTotalBytes)}];const components=advancedTable(dashboard?.components??[],[{label:'状态',render:row=>advancedTag(c.statusLabel(row.status),c.tagType(row.status))},{label:'组件',key:'name'},{label:'结果',key:'message'},{label:'延迟',render:row=>row.latencyMs==null?'—':row.latencyMs+' ms'},{label:'检查时间',render:row=>advancedDate(row.checkedAt)}]);const categories=advancedTable(dashboard?.categories??[],[{label:'分类',key:'name'},{label:'状态',render:row=>advancedTag(c.statusLabel(row.status),c.tagType(row.status))},{label:'正常',render:row=>(row.healthy??0)+' / '+(row.total??0)},{label:'健康率',render:row=>(row.total?Math.round(row.healthy*100/row.total):0)+'%'}]);const resources=[advancedKeyValue('工作集',formatBytes(dashboard?.host?.workingSetBytes)),advancedKeyValue('托管堆',formatBytes(dashboard?.host?.managedHeapBytes)),advancedKeyValue('线程数',dashboard?.host?.threadCount??0),advancedKeyValue('日志文件',(dashboard?.logging?.fileCount??0)+' 个 / '+formatBytes(dashboard?.logging?.totalSizeBytes)),advancedKeyValue('丢弃日志',dashboard?.logging?.droppedEntries??0)];return advancedPage('平台健康中心','汇总外部连接、任务、缓存、后台工作器、日志、进程和磁盘状态，并给出统一健康评分与修复建议。',[advancedButton('立即刷新',()=>c.load(false),{primary:true,disabled:c.loading})],[advancedMetrics(metrics),...advancedArray(dashboard?.remediations).map(item=>advancedAlert(item,'warning')),advancedCard([h('h3',{},'组件检查'),components]),advancedCard([h('h3',{},'分类健康度'),categories]),advancedCard([h('h3',{},'运行时资源'),...resources])])}

function renderSetupCenter(c){const readiness=advancedValue(c.readiness);const required=advancedArray(c.requiredChecks);const optional=advancedArray(c.optionalChecks);const requiredTable=advancedTable(required,[{label:'状态',render:row=>advancedTag(c.statusText(row),c.ready(row)?'success':'warning')},{label:'配置项',key:'label'},{label:'说明',key:'message'},{label:'操作',render:row=>!c.ready(row)&&row.actionRoute?advancedButton(row.actionLabel||'去配置',()=>c.navigate(row),{small:true,primary:true}):'—'}]);const optionalTable=advancedTable(optional,[{label:'状态',render:row=>advancedTag(c.statusText(row),c.ready(row)?'success':'info')},{label:'增强项',key:'label'},{label:'说明',key:'message'},{label:'操作',render:row=>!c.ready(row)&&row.actionRoute?advancedButton('配置',()=>c.navigate(row),{small:true,primary:true}):'—'}]);const metrics=[{label:'配置完成度',value:(readiness?.completionPercent??0)+'%',hint:readiness?.ready?'核心配置已完成':'仍需配置',tone:readiness?.ready?'success':'warning'},{label:'必须项',value:required.filter(item=>c.ready(item)).length+' / '+required.length},{label:'可选项',value:optional.filter(item=>c.ready(item)).length+' / '+optional.length},{label:'最近检测',value:advancedDate(readiness?.generatedAt)}];return advancedPage('首次使用配置中心','按依赖顺序完成服务器、RCON、存档、备份和可选组件配置；配置恢复可用后，各业务页面的提示会自动消失。',[advancedButton('重新检测',c.load,{disabled:c.loading}),advancedButton('打开系统设置',()=>c.router.push('/system/settings'),{primary:true})],[advancedMetrics(metrics),readiness?.blocked?advancedAlert('核心配置未完成。依赖这些连接或目录的功能将保持禁用，并显示就绪提示。','warning'):null,advancedCard([h('h3',{},advancedText(readiness?.title,'系统初始化')),h('p',{class:'description'},advancedText(readiness?.description,'正在读取配置状态…')),h('h3',{},'必须项'),requiredTable]),advancedCard([h('h3',{},'可选增强项'),optionalTable]),advancedArray(readiness?.recommendations).length?advancedCard([h('h3',{},'建议顺序'),h('ol',{},advancedArray(readiness.recommendations).map(item=>h('li',{},advancedText(item))))]):null])}

function advancedLogDateInput(c,index,placeholder){const current=advancedValue(c.dateRange);const value=current?.[index] instanceof Date&&!Number.isNaN(current[index].getTime())?new Date(current[index].getTime()-current[index].getTimezoneOffset()*60000).toISOString().slice(0,16):'';return h('input',{class:'adv-input',type:'datetime-local',value,placeholder,onChange:event=>{const next=Array.isArray(c.dateRange.value)?[...c.dateRange.value]:[new Date(),new Date()];if(event.target.value)next[index]=new Date(event.target.value);else{c.dateRange.value=null;return}c.dateRange.value=next}})}
function renderSystemLogs(c){const summary=advancedValue(c.summary);const metrics=[{label:'匹配日志',value:summary?.total??0},{label:'错误',value:summary?.errors??0,tone:(summary?.errors??0)>0?'danger':'default'},{label:'警告',value:summary?.warnings??0,tone:(summary?.warnings??0)>0?'warning':'default'},{label:'包含异常',value:summary?.withException??0,hint:'丢弃 '+(summary?.droppedEntries??0)+' 条'}];const levelOptions=[{value:'',label:'全部级别'},...['Trace','Debug','Information','Warning','Error','Critical'].map(value=>({value,label:value}))];const exceptionOptions=[{value:'',label:'全部异常状态'},{value:'true',label:'仅含异常'},{value:'false',label:'不含异常'}];const filters=advancedFilters([advancedSelectField(c.filters,'level',levelOptions),advancedInputField(c.filters,'category',{placeholder:'类别'}),advancedSelectField(c.filters,'hasException',exceptionOptions),advancedLogDateInput(c,0,'开始时间'),advancedLogDateInput(c,1,'结束时间'),advancedInputField(c.filters,'query',{placeholder:'搜索消息、类别或异常'}),advancedButton('查询',()=>c.load(true),{primary:true,disabled:c.loading}),advancedButton('重置',c.resetFilters,{disabled:c.loading})]);const table=advancedTable(c.entries,[{label:'时间',render:row=>advancedDate(row.timestamp)},{label:'级别',render:row=>advancedTag(row.level,c.levelType(row.level))},{label:'类别',key:'category'},{label:'事件 ID',key:'eventId'},{label:'消息',key:'message'},{label:'异常',render:row=>row.exception?advancedTag('有','danger'):'—'},{label:'操作',render:row=>advancedButton('详情',()=>c.showDetails(row),{small:true,primary:true})}]);const pagination=advancedActions([advancedButton('上一页',c.previous,{disabled:c.filters.page<=1}),h('span',{class:'muted'},advancedValue(c.pageLabel)),advancedButton('下一页',c.next,{disabled:!advancedBool(c.hasMore)})]);const expanded=advancedValue(c.expanded);const detail=advancedModal(c.drawerOpen,'日志详情',expanded?[advancedKeyValue('时间',advancedDate(expanded.timestamp)),advancedKeyValue('级别',expanded.level),advancedKeyValue('类别',expanded.category),advancedKeyValue('事件 ID',expanded.eventId),h('h3',{},'消息'),h('pre',{class:'log-code'},advancedText(expanded.message,'')),expanded.exception?h('div',{},[h('h3',{},'异常堆栈'),h('pre',{class:'log-code danger-text'},advancedText(expanded.exception,''))]):null]:[],[advancedButton('关闭',()=>{c.drawerOpen.value=false},{primary:true})]);const categories=advancedArray(summary?.topCategories);return advancedPage('系统日志中心','集中检索 API、后台任务、连接、审计和异常日志，支持时间、类别、异常筛选及 CSV/JSONL 导出。',[advancedButton('导出 CSV',()=>c.exportLogs('csv'),{primary:true,disabled:c.exporting}),advancedButton('导出 JSONL',()=>c.exportLogs('jsonl'),{disabled:c.exporting}),advancedButton('清空日志',c.clearLogs,{danger:true})],[advancedMetrics(metrics),advancedCard(filters),advancedCard([table,pagination]),categories.length?advancedCard([h('h3',{},'高频日志类别'),advancedList(categories.map(item=>item.category+' · '+item.count))]):null,detail])}

function renderAdvancedPage(name,context){const renderers={DiagnosticCenterPage:renderDiagnosticCenter,IncidentCenterPage:renderIncidentCenter,PlayerInsightsPage:renderPlayerInsights,WorldGovernancePage:renderWorldGovernance,DisasterRecoveryPage:renderDisasterRecovery,UpdateCenterPage:renderUpdateCenter,ConfigurationVersionsPage:renderConfigurationVersions,TaskCenterPage:renderTaskCenter,HealthCenterPage:renderHealthCenter,OperationsPlaybooksPage:renderOperationsPlaybooks,SecurityCenterPage:renderSecurityCenter,IntegrationCenterPage:renderIntegrationCenter,SetupCenterPage:renderSetupCenter,SystemLogsPage:renderSystemLogs};const renderer=renderers[name];if(!renderer)return advancedAlert('未知高级运维页面：'+name,'danger');const page=renderer(context);const notice=advancedReadinessNotice(context);if(!notice)return page;const children=Array.isArray(page.children)?page.children:[];return h('section',page.props,[children[0],notice,...children.slice(1)])}

const DiagnosticCenterPage=defineComponent({name:"DiagnosticCenterPage",setup(){
const readinessState = useAdvancedModuleReadiness('diagnostics');
const report = ref();
const loading = ref(false);
const operating = ref(false);
const category = ref('');
const canOperate = computed(() => ['Owner', 'Administrator', 'Operator'].includes(authStore.state.role));
const canAdminister = computed(() => ['Owner', 'Administrator'].includes(authStore.state.role));
const categories = computed(() => [...new Set((report.value?.checks ?? []).map(item => item.category))]);
const checks = computed(() => (report.value?.checks ?? []).filter(item => !category.value || item.category === category.value));
const metrics = computed(() => [
    { label: '正常检查', value: report.value?.healthyCount ?? 0, tone: 'success' },
    { label: '警告检查', value: report.value?.warningCount ?? 0, tone: 'warning' },
    { label: '严重异常', value: report.value?.criticalCount ?? 0, tone: 'danger' },
    { label: '总体状态', value: statusLabel(report.value?.overallStatus), hint: report.value ? formatDateTime(report.value.generatedAt) : '尚未运行' },
]);
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function statusLabel(status) {
    return { healthy: '正常', warning: '警告', critical: '严重', unknown: '未知' }[status ?? ''] ?? status ?? '未知';
}
function tagType(status) {
    if (status === 'healthy')
        return 'success';
    if (status === 'critical')
        return 'danger';
    if (status === 'warning')
        return 'warning';
    return 'info';
}
async function load(run = false) {
    readinessState.clearError();
    await readinessState.refresh();
    loading.value = true;
    try {
        report.value = (await (run ? api.diagnosticsRun() : api.diagnostics())).data;
    }
    catch (cause) {
        readinessState.captureError(cause);
    }
    finally {
        loading.value = false;
    }
}
async function run() {
    operating.value = true;
    try {
        await load(true);
        ElMessage.success('诊断体检已完成');
    }
    finally {
        operating.value = false;
    }
}
async function supportBundle() {
    operating.value = true;
    try {
        const result = await api.diagnosticsSupportBundle();
        const link = document.createElement('a');
        const url = URL.createObjectURL(result.blob);
        link.href = url;
        link.download = result.filename ?? `palops-support-${Date.now()}.zip`;
        link.click();
        URL.revokeObjectURL(url);
        ElMessage.success('诊断支持包已生成');
    }
    catch (cause) {
        ElMessage.error(`支持包生成失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
onMounted(() => load());
const advancedContext={readinessState,report,loading,operating,category,canOperate,canAdminister,categories,checks,metrics,message,statusLabel,tagType,load,run,supportBundle};return()=>renderAdvancedPage("DiagnosticCenterPage",advancedContext)
}});

const IncidentCenterPage=defineComponent({name:"IncidentCenterPage",setup(){
const readinessState = useAdvancedModuleReadiness('incidents');
const dashboard = ref();
const loading = ref(false);
const operating = ref(false);
const tab = ref('incidents');
const statusFilter = ref('active');
const createVisible = ref(false);
const ruleVisible = ref(false);
const editingRuleId = ref();
const incidentForm = reactive({ title: '', description: '', severity: 'medium', source: 'manual', fingerprint: '', assignee: '' });
const ruleForm = reactive({ name: '', enabled: true, sourceCode: 'palworldRest', triggerStatuses: ['unavailable'], severity: 'high', autoResolve: true });
const canOperate = computed(() => ['Owner', 'Administrator', 'Operator'].includes(authStore.state.role));
const canAdminister = computed(() => ['Owner', 'Administrator'].includes(authStore.state.role));
const metrics = computed(() => [
    { label: '待处理', value: dashboard.value?.openCount ?? 0, tone: 'danger' },
    { label: '处理中', value: dashboard.value?.acknowledgedCount ?? 0, tone: 'warning' },
    { label: '已恢复', value: dashboard.value?.resolvedCount ?? 0, tone: 'success' },
    { label: '严重故障', value: dashboard.value?.criticalCount ?? 0, tone: 'danger' },
]);
const incidents = computed(() => (dashboard.value?.incidents ?? []).filter(item => {
    if (statusFilter.value === 'active')
        return item.status !== 'resolved';
    if (statusFilter.value === 'all')
        return true;
    return item.status === statusFilter.value;
}));
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function tagType(value) {
    if (['critical', 'high', 'open', 'failed'].includes(value))
        return 'danger';
    if (['medium', 'acknowledged', 'warning'].includes(value))
        return 'warning';
    if (['resolved', 'healthy', 'low'].includes(value))
        return 'success';
    return 'info';
}
function label(value) {
    return { open: '待处理', acknowledged: '处理中', resolved: '已恢复', critical: '严重', high: '高', medium: '中', low: '低', information: '提示' }[value] ?? value;
}
async function load() {
    readinessState.clearError();
    await readinessState.refresh();
    loading.value = true;
    try {
        dashboard.value = (await api.incidents()).data;
    }
    catch (cause) {
        readinessState.captureError(cause);
    }
    finally {
        loading.value = false;
    }
}
function openCreate() {
    Object.assign(incidentForm, { title: '', description: '', severity: 'medium', source: 'manual', fingerprint: '', assignee: '' });
    createVisible.value = true;
}
async function createIncident() {
    if (!incidentForm.title.trim())
        return ElMessage.warning('请输入故障标题');
    operating.value = true;
    try {
        await api.createIncident({ ...incidentForm });
        createVisible.value = false;
        await load();
        ElMessage.success('故障已创建');
    }
    catch (cause) {
        ElMessage.error(`创建失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function action(item, actionName) {
    let text = '';
    try {
        const result = await ElMessageBox.prompt(actionName === 'assign' ? '输入处理人账号或名称' : '输入处理说明（会写入故障时间线）', `故障操作：${label(actionName)}`, { confirmButtonText: '确认', cancelButtonText: '取消', inputValue: actionName === 'assign' ? item.assignee : '' });
        text = result.value.trim();
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        await api.incidentAction(item.id, { action: actionName, message: actionName === 'assign' ? undefined : text, assignee: actionName === 'assign' ? text : undefined });
        await load();
        ElMessage.success('故障状态已更新');
    }
    catch (cause) {
        ElMessage.error(`操作失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
function openRule(item) {
    editingRuleId.value = item?.id;
    Object.assign(ruleForm, item ? {
        name: item.name, enabled: item.enabled, sourceCode: item.sourceCode,
        triggerStatuses: [...item.triggerStatuses], severity: item.severity, autoResolve: item.autoResolve,
    } : { name: '', enabled: true, sourceCode: 'palworldRest', triggerStatuses: ['unavailable'], severity: 'high', autoResolve: true });
    ruleVisible.value = true;
}
async function saveRule() {
    operating.value = true;
    try {
        await api.saveIncidentRule(editingRuleId.value, { ...ruleForm, triggerStatuses: [...ruleForm.triggerStatuses] });
        ruleVisible.value = false;
        await load();
        ElMessage.success('告警规则已保存');
    }
    catch (cause) {
        ElMessage.error(`保存失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function deleteRule(item) {
    try {
        await ElMessageBox.confirm(`确认删除规则“${item.name}”？`, '删除规则', { type: 'warning' });
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        await api.deleteIncidentRule(item.id);
        await load();
        ElMessage.success('规则已删除');
    }
    catch (cause) {
        ElMessage.error(`删除失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function evaluate() {
    operating.value = true;
    try {
        const result = (await api.evaluateIncidentHealth()).data;
        await load();
        ElMessage.success(`健康规则评估完成，变更 ${result.changed} 项`);
    }
    catch (cause) {
        ElMessage.error(`评估失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
onMounted(load);
const advancedContext={readinessState,dashboard,loading,operating,tab,statusFilter,createVisible,ruleVisible,editingRuleId,incidentForm,ruleForm,canOperate,canAdminister,metrics,incidents,message,tagType,label,load,openCreate,createIncident,action,openRule,saveRule,deleteRule,evaluate};return()=>renderAdvancedPage("IncidentCenterPage",advancedContext)
}});

const PlayerInsightsPage=defineComponent({name:"PlayerInsightsPage",setup(){
const readinessState = useAdvancedModuleReadiness('player-insights');
const dashboard = ref();
const loading = ref(false);
const operating = ref(false);
const keyword = ref('');
const risk = ref('');
const activity = ref('all');
const noteVisible = ref(false);
const noteText = ref('');
const selected = ref();
const canOperate = computed(() => ['Owner', 'Administrator', 'Operator'].includes(authStore.state.role));
const metrics = computed(() => [
    { label: '玩家总数', value: dashboard.value?.totalPlayers ?? 0 },
    { label: '当前在线', value: dashboard.value?.onlinePlayers ?? 0, tone: 'success' },
    { label: '高风险提示', value: dashboard.value?.highRiskPlayers ?? 0, tone: 'danger', hint: '仅辅助判断，不自动处罚' },
    { label: '长期未活动', value: dashboard.value?.inactivePlayers ?? 0, tone: 'warning' },
]);
const players = computed(() => {
    const needle = keyword.value.trim().toLowerCase();
    return (dashboard.value?.players ?? []).filter(item => {
        if (risk.value && item.riskLevel !== risk.value)
            return false;
        if (activity.value === 'online' && !item.online)
            return false;
        if (activity.value === 'offline' && item.online)
            return false;
        if (activity.value === 'inactive' && (!item.lastSeenAt || Date.now() - new Date(item.lastSeenAt).getTime() <= 30 * 86400000))
            return false;
        if (!needle)
            return true;
        return [item.name, item.userId, item.playerUid, item.guildName, item.notes, ...item.signals].some(value => String(value ?? '').toLowerCase().includes(needle));
    });
});
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function riskType(value) {
    if (value === 'high')
        return 'danger';
    if (value === 'medium')
        return 'warning';
    if (value === 'low')
        return 'success';
    return 'info';
}
function riskLabel(value) { return { high: '高', medium: '中', low: '低', none: '无' }[value] ?? value; }
async function load() {
    readinessState.clearError();
    await readinessState.refresh();
    loading.value = true;
    try {
        dashboard.value = (await api.playerInsights()).data;
    }
    catch (cause) {
        readinessState.captureError(cause);
    }
    finally {
        loading.value = false;
    }
}
function editNote(item) {
    selected.value = item;
    noteText.value = item.notes;
    noteVisible.value = true;
}
async function saveNote() {
    if (!selected.value)
        return;
    operating.value = true;
    try {
        const key = selected.value.userId || selected.value.playerUid;
        await api.updatePlayerInsightNote(key, noteText.value);
        noteVisible.value = false;
        await load();
        ElMessage.success('玩家洞察备注已保存');
    }
    catch (cause) {
        ElMessage.error(`保存失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
onMounted(load);
const advancedContext={readinessState,dashboard,loading,operating,keyword,risk,activity,noteVisible,noteText,selected,canOperate,metrics,players,message,riskType,riskLabel,load,editNote,saveNote};return()=>renderAdvancedPage("PlayerInsightsPage",advancedContext)
}});

const WorldGovernancePage=defineComponent({name:"WorldGovernancePage",setup(){
const readinessState = useAdvancedModuleReadiness('world-governance');
const dashboard = ref();
const loading = ref(false);
const operating = ref(false);
const keyword = ref('');
const type = ref('');
const reviewStatus = ref('pending');
const reviewVisible = ref(false);
const selected = ref();
const selectedStatus = ref('reviewed');
const reviewNote = ref('');
const canOperate = computed(() => ['Owner', 'Administrator', 'Operator'].includes(authStore.state.role));
const types = computed(() => [...new Set((dashboard.value?.candidates ?? []).map(item => item.candidateType))]);
const metrics = computed(() => [
    { label: '公会数量', value: dashboard.value?.guildCount ?? 0 },
    { label: '据点数量', value: dashboard.value?.baseCount ?? 0 },
    { label: '治理候选', value: dashboard.value?.candidateCount ?? 0, tone: 'warning' },
    { label: '严重候选', value: dashboard.value?.criticalCandidateCount ?? 0, tone: 'danger' },
]);
const candidates = computed(() => {
    const needle = keyword.value.trim().toLowerCase();
    return (dashboard.value?.candidates ?? []).filter(item => {
        if (type.value && item.candidateType !== type.value)
            return false;
        if (reviewStatus.value && item.reviewStatus !== reviewStatus.value)
            return false;
        if (!needle)
            return true;
        return [item.title, item.description, item.guildId, item.baseId, item.reviewNote].some(value => String(value ?? '').toLowerCase().includes(needle));
    });
});
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function severityType(value) {
    if (['critical', 'high'].includes(value))
        return 'danger';
    if (value === 'medium')
        return 'warning';
    if (value === 'low')
        return 'success';
    return 'info';
}
function reviewType(value) {
    if (value === 'reviewed')
        return 'success';
    if (value === 'ignored')
        return 'info';
    if (value === 'action-required')
        return 'danger';
    return 'warning';
}
async function load() {
    readinessState.clearError();
    await readinessState.refresh();
    loading.value = true;
    try {
        dashboard.value = (await api.worldGovernance()).data;
    }
    catch (cause) {
        readinessState.captureError(cause);
    }
    finally {
        loading.value = false;
    }
}
function openReview(item) {
    selected.value = item;
    selectedStatus.value = item.reviewStatus === 'pending' ? 'reviewed' : item.reviewStatus;
    reviewNote.value = item.reviewNote;
    reviewVisible.value = true;
}
async function saveReview() {
    if (!selected.value)
        return;
    operating.value = true;
    try {
        await api.reviewWorldGovernance(selected.value.id, selectedStatus.value, reviewNote.value);
        reviewVisible.value = false;
        await load();
        ElMessage.success('复核结果已保存');
    }
    catch (cause) {
        ElMessage.error(`保存失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
onMounted(load);
const advancedContext={readinessState,dashboard,loading,operating,keyword,type,reviewStatus,reviewVisible,selected,selectedStatus,reviewNote,canOperate,types,metrics,candidates,message,severityType,reviewType,load,openReview,saveReview};return()=>renderAdvancedPage("WorldGovernancePage",advancedContext)
}});

const DisasterRecoveryPage=defineComponent({name:"DisasterRecoveryPage",setup(){
const readinessState = useAdvancedModuleReadiness('disaster-recovery');
const dashboard = ref();
const backups = ref([]);
const loading = ref(false);
const operating = ref(false);
const tab = ref('targets');
const editorVisible = ref(false);
const editingId = ref();
const form = reactive({ name: '', targetType: 'local', endpoint: '', credentialReference: '', enabled: true, retentionCount: 10 });
const canAdminister = computed(() => ['Owner', 'Administrator'].includes(authStore.state.role));
const isOwner = computed(() => authStore.state.role === 'Owner');
const metrics = computed(() => [
    { label: '灾备目标', value: dashboard.value?.totalTargets ?? 0 },
    { label: '启用目标', value: dashboard.value?.enabledTargets ?? 0, tone: 'success' },
    { label: '成功演练', value: dashboard.value?.successfulDrills ?? 0, tone: 'success' },
    { label: '失败演练', value: dashboard.value?.failedDrills ?? 0, tone: 'danger' },
]);
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function tagType(status) {
    if (['healthy', 'succeeded'].includes(status))
        return 'success';
    if (['critical', 'failed'].includes(status))
        return 'danger';
    if (status === 'warning')
        return 'warning';
    return 'info';
}
async function load() {
    readinessState.clearError();
    await readinessState.refresh();
    loading.value = true;
    try {
        const [dr, records] = await Promise.all([api.disasterRecovery(), api.backups()]);
        dashboard.value = dr.data;
        backups.value = records.data.filter(item => item.status === 'completed' || item.status === 'verified');
    }
    catch (cause) {
        readinessState.captureError(cause);
    }
    finally {
        loading.value = false;
    }
}
function openEditor(item) {
    editingId.value = item?.id;
    Object.assign(form, item ? {
        name: item.name, targetType: item.targetType, endpoint: item.endpoint,
        credentialReference: item.credentialReference, enabled: item.enabled, retentionCount: item.retentionCount,
    } : { name: '', targetType: 'local', endpoint: '', credentialReference: '', enabled: true, retentionCount: 10 });
    editorVisible.value = true;
}
async function save() {
    operating.value = true;
    try {
        if (editingId.value)
            await api.updateDisasterRecoveryTarget(editingId.value, { ...form });
        else
            await api.createDisasterRecoveryTarget({ ...form });
        editorVisible.value = false;
        await load();
        ElMessage.success('灾备目标已保存');
    }
    catch (cause) {
        ElMessage.error(`保存失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function validateTarget(item) {
    operating.value = true;
    try {
        const target = (await api.validateDisasterRecoveryTarget(item.id)).data;
        await load();
        ElMessage[target.lastValidationStatus === 'healthy' ? 'success' : 'warning'](target.lastValidationMessage);
    }
    catch (cause) {
        ElMessage.error(`验证失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function drill(item) {
    if (!backups.value.length)
        return ElMessage.warning('没有可用于演练的完成备份');
    let backupId = backups.value[0].id;
    try {
        const result = await ElMessageBox.prompt(`输入备份 ID。可用的最新备份：${backups.value.slice(0, 5).map(value => `${value.fileName} (${value.id})`).join('；')}`, `灾备演练 · ${item.name}`, { inputValue: backupId, confirmButtonText: '开始演练', cancelButtonText: '取消', type: 'warning' });
        backupId = result.value.trim();
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        const result = (await api.runDisasterRecoveryDrill(item.id, backupId)).data;
        await load();
        ElMessage[result.status === 'succeeded' ? 'success' : 'error'](result.message);
    }
    catch (cause) {
        ElMessage.error(`演练失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function remove(item) {
    try {
        await ElMessageBox.confirm(`确认删除灾备目标“${item.name}”？历史演练记录会保留。`, '删除灾备目标', { type: 'warning' });
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        await api.deleteDisasterRecoveryTarget(item.id);
        await load();
        ElMessage.success('灾备目标已删除');
    }
    catch (cause) {
        ElMessage.error(`删除失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
onMounted(load);
const advancedContext={readinessState,dashboard,backups,loading,operating,tab,editorVisible,editingId,form,canAdminister,isOwner,metrics,message,tagType,load,openEditor,save,validateTarget,drill,remove};return()=>renderAdvancedPage("DisasterRecoveryPage",advancedContext)
}});

const UpdateCenterPage=defineComponent({name:"UpdateCenterPage",setup(){
const readinessState = useAdvancedModuleReadiness('update-center');
const dashboard = ref();
const loading = ref(false);
const operating = ref(false);
const tab = ref('components');
const editorVisible = ref(false);
const editingId = ref();
const form = reactive({ name: '', targetComponent: 'PalOps Web', targetVersion: '', compatibilityAcknowledged: false, note: '' });
const canAdminister = computed(() => ['Owner', 'Administrator'].includes(authStore.state.role));
const isOwner = computed(() => authStore.state.role === 'Owner');
const metrics = computed(() => [
    { label: '检测组件', value: dashboard.value?.components.length ?? 0 },
    { label: '可用更新', value: dashboard.value?.components.filter(item => item.updateAvailable).length ?? 0, tone: 'warning' },
    { label: '更新计划', value: dashboard.value?.plans.length ?? 0 },
    { label: '预检状态', value: dashboard.value?.preflight.allowed ? '允许' : '阻止', tone: dashboard.value?.preflight.allowed ? 'success' : 'danger' },
]);
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function tagType(value) {
    if (['up-to-date', 'healthy', 'approved', 'detected'].includes(value))
        return 'success';
    if (['blocked', 'critical', 'failed'].includes(value))
        return 'danger';
    if (['update-available', 'warning', 'draft'].includes(value))
        return 'warning';
    return 'info';
}
async function load(force = false) {
    readinessState.clearError();
    await readinessState.refresh();
    loading.value = true;
    try {
        dashboard.value = (await api.updateCenter(force)).data;
    }
    catch (cause) {
        readinessState.captureError(cause);
    }
    finally {
        loading.value = false;
    }
}
async function preflight() {
    operating.value = true;
    try {
        const result = (await api.updatePreflight()).data;
        await load();
        ElMessage[result.allowed ? 'success' : 'warning'](result.allowed ? '更新预检通过' : `预检被阻止：${result.blockingReasons.join('；')}`);
    }
    catch (cause) {
        ElMessage.error(`预检失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
function openEditor(item) {
    editingId.value = item?.id;
    Object.assign(form, item ? {
        name: item.name, targetComponent: item.targetComponent, targetVersion: item.targetVersion,
        compatibilityAcknowledged: item.compatibilityAcknowledged, note: item.note,
    } : { name: '', targetComponent: 'PalOps Web', targetVersion: '', compatibilityAcknowledged: false, note: '' });
    editorVisible.value = true;
}
async function save() {
    operating.value = true;
    try {
        if (editingId.value)
            await api.updateUpdatePlan(editingId.value, { ...form });
        else
            await api.createUpdatePlan({ ...form });
        editorVisible.value = false;
        await load();
        ElMessage.success('更新计划已保存');
    }
    catch (cause) {
        ElMessage.error(`保存失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function approve(item) {
    let confirmation = '';
    try {
        confirmation = (await ElMessageBox.prompt('请输入 RUN UPDATE PLAN。批准只完成健康、备份和兼容性预检，不会静默替换程序文件。', `批准更新计划 · ${item.name}`, { inputPlaceholder: 'RUN UPDATE PLAN', type: 'warning' })).value.trim();
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        const plan = (await api.approveUpdatePlan(item.id, confirmation)).data;
        await load();
        ElMessage[plan.status === 'approved' ? 'success' : 'warning'](plan.lastMessage);
    }
    catch (cause) {
        ElMessage.error(`批准失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function remove(item) {
    try {
        await ElMessageBox.confirm(`确认删除更新计划“${item.name}”？`, '删除更新计划', { type: 'warning' });
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        await api.deleteUpdatePlan(item.id);
        await load();
        ElMessage.success('更新计划已删除');
    }
    catch (cause) {
        ElMessage.error(`删除失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
onMounted(() => load());
const advancedContext={readinessState,dashboard,loading,operating,tab,editorVisible,editingId,form,canAdminister,isOwner,metrics,message,tagType,load,preflight,openEditor,save,approve,remove};return()=>renderAdvancedPage("UpdateCenterPage",advancedContext)
}});

const ConfigurationVersionsPage=defineComponent({name:"ConfigurationVersionsPage",setup(){
const readinessState = useAdvancedModuleReadiness('configuration-versions');
const dashboard = ref();
const diff = ref();
const loading = ref(false);
const operating = ref(false);
const createVisible = ref(false);
const form = reactive({ name: '', note: '' });
const compare = reactive({ fromId: '', toId: '' });
const isOwner = computed(() => authStore.state.role === 'Owner');
const canAdminister = computed(() => ['Owner', 'Administrator'].includes(authStore.state.role));
const metrics = computed(() => [
    { label: '版本快照', value: dashboard.value?.versions.length ?? 0 },
    { label: '匹配当前配置', value: dashboard.value?.versions.filter(item => item.currentMatch).length ?? 0, tone: 'success' },
    { label: '当前哈希', value: dashboard.value?.currentSha256?.slice(0, 12) || '—' },
    { label: '差异状态', value: diff.value ? (diff.value.identical ? '一致' : `${diff.value.changedKeys.length + diff.value.changedSettingKeys.length} 项变化`) : '未比较', tone: diff.value?.identical ? 'success' : diff.value ? 'warning' : undefined },
]);
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
async function load() {
    readinessState.clearError();
    await readinessState.refresh();
    loading.value = true;
    try {
        dashboard.value = (await api.configurationVersions()).data;
        const versions = dashboard.value.versions;
        if (!compare.fromId && versions.length > 1)
            compare.fromId = versions[1].id;
        if (!compare.toId && versions.length > 0)
            compare.toId = versions[0].id;
    }
    catch (cause) {
        readinessState.captureError(cause);
    }
    finally {
        loading.value = false;
    }
}
async function createSnapshot() {
    operating.value = true;
    try {
        await api.createConfigurationVersion(form.name, form.note);
        createVisible.value = false;
        form.name = '';
        form.note = '';
        await load();
        ElMessage.success('配置快照已创建');
    }
    catch (cause) {
        ElMessage.error(`创建失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function compareVersions() {
    if (!compare.fromId || !compare.toId || compare.fromId === compare.toId) {
        ElMessage.warning('请选择两个不同版本');
        return;
    }
    operating.value = true;
    try {
        diff.value = (await api.configurationVersionDiff(compare.fromId, compare.toId)).data;
    }
    catch (cause) {
        ElMessage.error(`比较失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function restore(item) {
    let confirmation = '';
    let restart = false;
    try {
        confirmation = (await ElMessageBox.prompt('请输入 RESTORE CONFIGURATION。恢复前会保留当前配置快照。', `恢复配置 · ${item.name}`, { inputPlaceholder: 'RESTORE CONFIGURATION', type: 'warning' })).value.trim();
        restart = await ElMessageBox.confirm('恢复后是否立即请求重启服务器？', '重启选项', { confirmButtonText: '恢复并重启', cancelButtonText: '仅恢复', distinguishCancelAndClose: true, type: 'warning' }).then(() => true).catch(action => action === 'cancel' ? false : Promise.reject(action));
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        await api.restoreConfigurationVersion(item.id, confirmation, restart);
        await load();
        ElMessage.success('配置已恢复');
    }
    catch (cause) {
        ElMessage.error(`恢复失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function remove(item) {
    try {
        await ElMessageBox.confirm(`确认删除快照“${item.name}”？`, '删除配置快照', { type: 'warning' });
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        await api.deleteConfigurationVersion(item.id);
        await load();
        ElMessage.success('快照已删除');
    }
    catch (cause) {
        ElMessage.error(`删除失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
onMounted(load);
const advancedContext={readinessState,dashboard,diff,loading,operating,createVisible,form,compare,isOwner,canAdminister,metrics,message,load,createSnapshot,compareVersions,restore,remove};return()=>renderAdvancedPage("ConfigurationVersionsPage",advancedContext)
}});

const TaskCenterPage=defineComponent({name:"TaskCenterPage",setup(){
const dashboard = ref();
const loading = ref(false);
const operatingId = ref('');
const statusFilter = ref('');
let timer;
const canOperate = computed(() => ['Owner', 'Administrator', 'Operator'].includes(authStore.state.role));
const items = computed(() => (dashboard.value?.items ?? []).filter(item => !statusFilter.value || item.status === statusFilter.value));
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function statusLabel(status) {
    return { queued: '排队中', running: '执行中', completed: '已完成', failed: '失败', cancelled: '已取消', timedOut: '已超时', interrupted: '已中断' }[status] ?? status;
}
function statusType(status) {
    if (status === 'completed')
        return 'success';
    if (status === 'failed' || status === 'timedOut')
        return 'danger';
    if (status === 'running' || status === 'queued')
        return 'warning';
    return 'info';
}
function duration(row) {
    const value = row.durationMilliseconds ?? (row.startedAt ? Math.max(0, Date.now() - new Date(row.startedAt).getTime()) : undefined);
    if (value === undefined)
        return '—';
    const seconds = Math.floor(value / 1000);
    const minutes = Math.floor(seconds / 60);
    return minutes ? `${minutes}分 ${seconds % 60}秒` : `${seconds}秒`;
}
async function load(silent = false) {
    if (!silent)
        loading.value = true;
    try {
        dashboard.value = (await api.platformTasks()).data;
    }
    catch (cause) {
        if (!silent)
            ElMessage.error(`任务中心加载失败：${message(cause)}`);
    }
    finally {
        if (!silent)
            loading.value = false;
    }
}
async function cancel(row) {
    operatingId.value = row.id;
    try {
        await api.cancelPlatformTask(row.id);
        ElMessage.success('取消请求已提交');
        await load(true);
    }
    catch (cause) {
        ElMessage.error(`取消失败：${message(cause)}`);
    }
    finally {
        operatingId.value = '';
    }
}
async function retry(row) {
    operatingId.value = row.id;
    try {
        await api.retryPlatformTask(row.id);
        ElMessage.success('任务已重新排队');
        await load(true);
    }
    catch (cause) {
        ElMessage.error(`重试失败：${message(cause)}`);
    }
    finally {
        operatingId.value = '';
    }
}
onMounted(() => {
    void load();
    timer = window.setInterval(() => void load(true), 5000);
});
onBeforeUnmount(() => { if (timer)
    window.clearInterval(timer); });
const advancedContext={dashboard,loading,operatingId,statusFilter,timer,canOperate,items,message,statusLabel,statusType,duration,load,cancel,retry};return()=>renderAdvancedPage("TaskCenterPage",advancedContext)
}});

const HealthCenterPage=defineComponent({name:"HealthCenterPage",setup(){
const dashboard = ref();
const loading = ref(false);
let timer;
const scoreStatus = computed(() => {
    const score = dashboard.value?.score ?? 0;
    return score >= 90 ? 'success' : score >= 70 ? 'warning' : 'exception';
});
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function statusLabel(status) {
    return { healthy: '正常', degraded: '降级', unavailable: '不可用', notConfigured: '未配置', critical: '严重', warning: '警告', unknown: '未知' }[status ?? ''] ?? status ?? '未知';
}
function tagType(status) {
    if (status === 'healthy')
        return 'success';
    if (status === 'degraded' || status === 'warning' || status === 'notConfigured')
        return 'warning';
    if (status === 'unavailable' || status === 'critical')
        return 'danger';
    return 'info';
}
async function load(silent = false) {
    if (!silent)
        loading.value = true;
    try {
        dashboard.value = (await api.healthDashboard()).data;
    }
    catch (cause) {
        if (!silent)
            ElMessage.error(`健康状态加载失败：${message(cause)}`);
    }
    finally {
        if (!silent)
            loading.value = false;
    }
}
onMounted(() => {
    void load();
    timer = window.setInterval(() => void load(true), 10000);
});
onBeforeUnmount(() => { if (timer)
    window.clearInterval(timer); });
const advancedContext={dashboard,loading,timer,scoreStatus,message,statusLabel,tagType,load};return()=>renderAdvancedPage("HealthCenterPage",advancedContext)
}});

const OperationsPlaybooksPage=defineComponent({name:"OperationsPlaybooksPage",setup(){
const readinessState = useAdvancedModuleReadiness('operations-playbooks');
const dashboard = ref();
const loading = ref(false);
const operating = ref(false);
const tab = ref('playbooks');
const editorVisible = ref(false);
const editingId = ref();
const form = reactive({ name: '', description: '', enabled: true, steps: [] });
const canEdit = computed(() => ['Owner', 'Administrator'].includes(authStore.state.role));
const isOwner = computed(() => authStore.state.role === 'Owner');
const metrics = computed(() => [
    { label: '运维剧本', value: dashboard.value?.totalPlaybooks ?? 0 },
    { label: '已启用', value: dashboard.value?.enabledPlaybooks ?? 0, tone: 'success' },
    { label: '成功运行', value: dashboard.value?.successfulRuns ?? 0, tone: 'success' },
    { label: '失败运行', value: dashboard.value?.failedRuns ?? 0, tone: 'danger' },
]);
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function statusType(status) {
    if (status === 'succeeded')
        return 'success';
    if (status === 'failed')
        return 'danger';
    if (status === 'running')
        return 'warning';
    return 'info';
}
function parametersText(parameters) { return Object.keys(parameters).length ? JSON.stringify(parameters, null, 2) : '{}'; }
function parseParameters(value, index) {
    let parsed;
    try {
        parsed = JSON.parse(value || '{}');
    }
    catch {
        throw new Error(`第 ${index + 1} 步参数不是有效 JSON`);
    }
    if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object')
        throw new Error(`第 ${index + 1} 步参数必须是 JSON 对象`);
    const result = {};
    for (const [key, item] of Object.entries(parsed))
        result[key] = String(item);
    return result;
}
async function load() {
    readinessState.clearError();
    await readinessState.refresh();
    loading.value = true;
    try {
        dashboard.value = (await api.operationsPlaybooks()).data;
    }
    catch (cause) {
        readinessState.captureError(cause);
    }
    finally {
        loading.value = false;
    }
}
function addStep() { form.steps.push({ action: dashboard.value?.allowedActions[0] || 'health-refresh', parametersText: '{}', continueOnError: false }); }
function moveStep(index, direction) {
    const target = index + direction;
    if (target < 0 || target >= form.steps.length)
        return;
    const [item] = form.steps.splice(index, 1);
    form.steps.splice(target, 0, item);
}
function openEditor(item) {
    editingId.value = item?.id;
    form.name = item?.name || '';
    form.description = item?.description || '';
    form.enabled = item?.enabled ?? true;
    form.steps = item?.steps.map(step => ({ action: step.action, parametersText: parametersText(step.parameters), continueOnError: step.continueOnError })) ?? [];
    if (!form.steps.length)
        addStep();
    editorVisible.value = true;
}
function buildRequest() {
    if (!form.steps.length)
        throw new Error('至少需要一个步骤');
    const steps = form.steps.map((step, index) => ({ order: index + 1, action: step.action, parameters: parseParameters(step.parametersText, index), continueOnError: step.continueOnError }));
    return { name: form.name, description: form.description, enabled: form.enabled, steps };
}
async function save() {
    operating.value = true;
    try {
        const payload = buildRequest();
        if (editingId.value)
            await api.updateOperationsPlaybook(editingId.value, payload);
        else
            await api.createOperationsPlaybook(payload);
        editorVisible.value = false;
        await load();
        ElMessage.success('运维剧本已保存');
    }
    catch (cause) {
        ElMessage.error(`保存失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function run(item) {
    let confirmation = '';
    if (item.steps.some(step => step.action === 'maintenance-run')) {
        try {
            confirmation = (await ElMessageBox.prompt('该剧本包含维护流程，请输入 RUN PLAYBOOK。', `运行剧本 · ${item.name}`, { inputPlaceholder: 'RUN PLAYBOOK', type: 'warning' })).value.trim();
        }
        catch {
            return;
        }
    }
    else {
        try {
            await ElMessageBox.confirm(`确认运行“${item.name}”？`, '运行运维剧本', { type: 'warning' });
        }
        catch {
            return;
        }
    }
    operating.value = true;
    try {
        const result = (await api.runOperationsPlaybook(item.id, confirmation)).data;
        await load();
        ElMessage[result.status === 'succeeded' ? 'success' : 'warning'](result.message);
    }
    catch (cause) {
        ElMessage.error(`运行失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function remove(item) {
    try {
        await ElMessageBox.confirm(`确认删除“${item.name}”？`, '删除运维剧本', { type: 'warning' });
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        await api.deleteOperationsPlaybook(item.id);
        await load();
        ElMessage.success('运维剧本已删除');
    }
    catch (cause) {
        ElMessage.error(`删除失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
onMounted(load);
const advancedContext={readinessState,dashboard,loading,operating,tab,editorVisible,editingId,form,canEdit,isOwner,metrics,message,statusType,parametersText,parseParameters,load,addStep,moveStep,openEditor,buildRequest,save,run,remove};return()=>renderAdvancedPage("OperationsPlaybooksPage",advancedContext)
}});

const SecurityCenterPage=defineComponent({name:"SecurityCenterPage",setup(){
const readinessState = useAdvancedModuleReadiness('security-center');
const dashboard = ref();
const loading = ref(false);
const operating = ref(false);
const tab = ref('tokens');
const policyVisible = ref(false);
const tokenVisible = ref(false);
const tokenResultVisible = ref(false);
const createdToken = ref();
const isOwner = computed(() => authStore.state.role === 'Owner');
const availableScopes = ['status.read', 'events.write', 'incidents.read', 'diagnostics.read'];
const policyForm = reactive({ apiTokensEnabled: true, maximumTokenDays: 90, requireHighRiskConfirmation: true, auditTokenUse: true, maximumActiveTokens: 20 });
const tokenForm = reactive({ name: '', scopes: ['status.read'], expiresInDays: 30 });
const metrics = computed(() => [
    { label: '活动令牌', value: dashboard.value?.activeTokens ?? 0, tone: 'success' },
    { label: '即将过期', value: dashboard.value?.expiringTokens ?? 0, tone: 'warning' },
    { label: '已吊销', value: dashboard.value?.revokedTokens ?? 0 },
    { label: '近期安全事件', value: dashboard.value?.recentObservations.length ?? 0 },
]);
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function tokenStatus(item) {
    if (item.revokedAt)
        return { label: '已吊销', type: 'danger' };
    if (new Date(item.expiresAt).getTime() <= Date.now() + 7 * 86400000)
        return { label: '即将过期', type: 'warning' };
    return { label: '有效', type: 'success' };
}
async function load() {
    readinessState.clearError();
    await readinessState.refresh();
    loading.value = true;
    try {
        dashboard.value = (await api.securityCenter()).data;
    }
    catch (cause) {
        readinessState.captureError(cause);
    }
    finally {
        loading.value = false;
    }
}
function openPolicy() {
    if (!dashboard.value)
        return;
    Object.assign(policyForm, dashboard.value.policy);
    policyVisible.value = true;
}
async function savePolicy() {
    operating.value = true;
    try {
        await api.updateSecurityPolicy({ ...policyForm });
        policyVisible.value = false;
        await load();
        ElMessage.success('安全策略已保存');
    }
    catch (cause) {
        ElMessage.error(`保存失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
function openToken() {
    tokenForm.name = '';
    tokenForm.scopes = ['status.read'];
    tokenForm.expiresInDays = Math.min(30, dashboard.value?.policy.maximumTokenDays ?? 30);
    tokenVisible.value = true;
}
async function createToken() {
    operating.value = true;
    try {
        createdToken.value = (await api.createApiToken({ ...tokenForm, scopes: [...tokenForm.scopes] })).data;
        tokenVisible.value = false;
        tokenResultVisible.value = true;
        await load();
    }
    catch (cause) {
        ElMessage.error(`创建令牌失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function copyToken() {
    if (!createdToken.value?.plainText)
        return;
    try {
        await navigator.clipboard.writeText(createdToken.value.plainText);
        ElMessage.success('令牌已复制');
    }
    catch {
        ElMessage.warning('复制失败，请手动复制');
    }
}
function closeTokenResult() { tokenResultVisible.value = false; createdToken.value = undefined; }
async function revoke(item) {
    let confirmation = '';
    try {
        confirmation = (await ElMessageBox.prompt('请输入 REVOKE TOKEN。吊销后无法恢复。', `吊销令牌 · ${item.name}`, { inputPlaceholder: 'REVOKE TOKEN', type: 'warning' })).value.trim();
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        await api.revokeApiToken(item.id, confirmation);
        await load();
        ElMessage.success('令牌已吊销');
    }
    catch (cause) {
        ElMessage.error(`吊销失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
onMounted(load);
const advancedContext={readinessState,dashboard,loading,operating,tab,policyVisible,tokenVisible,tokenResultVisible,createdToken,isOwner,availableScopes,policyForm,tokenForm,metrics,message,tokenStatus,load,openPolicy,savePolicy,openToken,createToken,copyToken,closeTokenResult,revoke};return()=>renderAdvancedPage("SecurityCenterPage",advancedContext)
}});

const IntegrationCenterPage=defineComponent({name:"IntegrationCenterPage",setup(){
const readinessState = useAdvancedModuleReadiness('integration-center');
const dashboard = ref();
const loading = ref(false);
const operating = ref(false);
const tab = ref('subscriptions');
const editorVisible = ref(false);
const editingId = ref();
const form = reactive({ name: '', enabled: true, eventTypesText: '', destination: '', signingSecretReference: '' });
const canEdit = computed(() => ['Owner', 'Administrator'].includes(authStore.state.role));
const isOwner = computed(() => authStore.state.role === 'Owner');
const metrics = computed(() => [
    { label: '集成订阅', value: dashboard.value?.totalSubscriptions ?? 0 },
    { label: '已启用', value: dashboard.value?.enabledSubscriptions ?? 0, tone: 'success' },
    { label: '已接收事件', value: dashboard.value?.acceptedEvents ?? 0, tone: 'success' },
    { label: '拒绝事件', value: dashboard.value?.rejectedEvents ?? 0, tone: 'danger' },
]);
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function eventTypes(value) { return [...new Set(value.split(/[\n,]/).map(item => item.trim()).filter(Boolean))]; }
function statusType(value) {
    if (['accepted', 'delivered', 'healthy'].includes(value))
        return 'success';
    if (['rejected', 'failed', 'blocked'].includes(value))
        return 'danger';
    if (['pending', 'warning'].includes(value))
        return 'warning';
    return 'info';
}
async function load() {
    readinessState.clearError();
    await readinessState.refresh();
    loading.value = true;
    try {
        dashboard.value = (await api.integrationCenter()).data;
    }
    catch (cause) {
        readinessState.captureError(cause);
    }
    finally {
        loading.value = false;
    }
}
function openEditor(item) {
    editingId.value = item?.id;
    form.name = item?.name || '';
    form.enabled = item?.enabled ?? true;
    form.eventTypesText = item?.eventTypes.join('\n') || 'server.health.changed';
    form.destination = item?.destination || '';
    form.signingSecretReference = item?.signingSecretReference || '';
    editorVisible.value = true;
}
async function save() {
    const payload = {
        name: form.name, enabled: form.enabled, eventTypes: eventTypes(form.eventTypesText),
        destination: form.destination, signingSecretReference: form.signingSecretReference,
    };
    operating.value = true;
    try {
        if (editingId.value)
            await api.updateIntegrationSubscription(editingId.value, payload);
        else
            await api.createIntegrationSubscription(payload);
        editorVisible.value = false;
        await load();
        ElMessage.success('集成订阅已保存');
    }
    catch (cause) {
        ElMessage.error(`保存失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
async function remove(item) {
    try {
        await ElMessageBox.confirm(`确认删除订阅“${item.name}”？`, '删除集成订阅', { type: 'warning' });
    }
    catch {
        return;
    }
    operating.value = true;
    try {
        await api.deleteIntegrationSubscription(item.id);
        await load();
        ElMessage.success('集成订阅已删除');
    }
    catch (cause) {
        ElMessage.error(`删除失败：${message(cause)}`);
    }
    finally {
        operating.value = false;
    }
}
onMounted(load);
const advancedContext={readinessState,dashboard,loading,operating,tab,editorVisible,editingId,form,canEdit,isOwner,metrics,message,eventTypes,statusType,load,openEditor,save,remove};return()=>renderAdvancedPage("IntegrationCenterPage",advancedContext)
}});

const SetupCenterPage=defineComponent({name:"SetupCenterPage",setup(){
const router = useRouter();
const readiness = ref();
const loading = ref(false);
const requiredChecks = computed(() => (readiness.value?.checks ?? []).filter(item => item.required));
const optionalChecks = computed(() => (readiness.value?.checks ?? []).filter(item => !item.required));
function message(cause) { return cause instanceof Error ? cause.message : String(cause); }
function ready(check) { return check.status === 'ready'; }
function statusText(check) { return ready(check) ? '已完成' : check.status === 'disabled' ? '已禁用' : '待配置'; }
async function load() {
    loading.value = true;
    try {
        readiness.value = (await api.configurationReadiness('setup-center')).data;
    }
    catch (cause) {
        ElMessage.error(`配置清单加载失败：${message(cause)}`);
    }
    finally {
        loading.value = false;
    }
}
async function navigate(check) {
    if (!check.actionRoute)
        return;
    await router.push(check.actionRoute);
}
onMounted(load);
const advancedContext={router,readiness,loading,requiredChecks,optionalChecks,message,ready,statusText,load,navigate};return()=>renderAdvancedPage("SetupCenterPage",advancedContext)
}});

const SystemLogsPage=defineComponent({name:"SystemLogsPage",setup(){
const entries = ref([]);
const summary = ref();
const loading = ref(false);
const exporting = ref(false);
const hasMore = ref(false);
const drawerOpen = ref(false);
const expanded = ref();
const dateRange = ref(null);
const filters = reactive({ page: 1, pageSize: 100, level: '', category: '', query: '', hasException: '' });
const pageLabel = computed(() => `第 ${filters.page} 页`);
const queryFilters = computed(() => ({
    page: filters.page,
    pageSize: filters.pageSize,
    level: filters.level || undefined,
    category: filters.category || undefined,
    query: filters.query || undefined,
    from: dateRange.value?.[0]?.toISOString(),
    to: dateRange.value?.[1]?.toISOString(),
    hasException: filters.hasException === '' ? undefined : filters.hasException === 'true',
}));
function errorMessage(error, fallback) { return error instanceof Error ? error.message : fallback; }
function levelType(level) {
    if (/critical|error/i.test(level))
        return 'danger';
    if (/warn/i.test(level))
        return 'warning';
    if (/information|info/i.test(level))
        return 'success';
    return 'info';
}
function showDetails(row) { expanded.value = row; drawerOpen.value = true; }
async function load(resetPage = false) {
    if (resetPage)
        filters.page = 1;
    loading.value = true;
    try {
        const [page, totals] = await Promise.all([
            api.systemLogs(queryFilters.value),
            api.systemLogSummary(queryFilters.value),
        ]);
        entries.value = page.data.entries;
        hasMore.value = page.data.hasMore;
        summary.value = totals.data;
    }
    catch (error) {
        ElMessage.error(errorMessage(error, '系统日志加载失败'));
    }
    finally {
        loading.value = false;
    }
}
async function clearLogs() {
    try {
        const result = await ElMessageBox.prompt('清空全部系统日志不可撤销，请输入 CONFIRM。', '清空系统日志', {
            confirmButtonText: '清空', cancelButtonText: '取消', inputPattern: /^CONFIRM$/, inputErrorMessage: '请输入 CONFIRM', type: 'warning',
        });
        await api.clearSystemLogs(result.value);
        ElMessage.success('系统日志已清空');
        await load(true);
    }
    catch (error) {
        if (error === 'cancel' || error === 'close')
            return;
        ElMessage.error(errorMessage(error, '系统日志清空失败'));
    }
}
async function exportLogs(format) {
    exporting.value = true;
    try {
        const result = await api.exportSystemLogs(queryFilters.value, format);
        const link = document.createElement('a');
        const url = URL.createObjectURL(result.blob);
        link.href = url;
        link.download = result.filename ?? `palops-system-logs-${Date.now()}.${format}`;
        link.click();
        URL.revokeObjectURL(url);
        ElMessage.success('日志导出完成');
    }
    catch (error) {
        ElMessage.error(errorMessage(error, '日志导出失败'));
    }
    finally {
        exporting.value = false;
    }
}
function handleExportCommand(command) {
    void exportLogs(command === 'jsonl' ? 'jsonl' : 'csv');
}
function previous() { if (filters.page > 1) {
    filters.page -= 1;
    void load();
} }
function next() { if (hasMore.value) {
    filters.page += 1;
    void load();
} }
function resetFilters() {
    Object.assign(filters, { page: 1, pageSize: 100, level: '', category: '', query: '', hasException: '' });
    dateRange.value = null;
    void load();
}
onMounted(() => load());
const advancedContext={entries,summary,loading,exporting,hasMore,drawerOpen,expanded,dateRange,filters,pageLabel,queryFilters,errorMessage,levelType,showDetails,load,clearLogs,exportLogs,handleExportCommand,previous,next,resetFilters};return()=>renderAdvancedPage("SystemLogsPage",advancedContext)
}});
export{DiagnosticCenterPage,IncidentCenterPage,PlayerInsightsPage,WorldGovernancePage,DisasterRecoveryPage,UpdateCenterPage,ConfigurationVersionsPage,TaskCenterPage,HealthCenterPage,OperationsPlaybooksPage,SecurityCenterPage,IntegrationCenterPage,SetupCenterPage,SystemLogsPage};
