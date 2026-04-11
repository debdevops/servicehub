var ms=t=>{throw TypeError(t)};var Ot=(t,e,s)=>e.has(t)||ms("Cannot "+s);var r=(t,e,s)=>(Ot(t,e,"read from private field"),s?s.call(t):e.get(t)),x=(t,e,s)=>e.has(t)?ms("Cannot add the same private member more than once"):e instanceof WeakSet?e.add(t):e.set(t,s),d=(t,e,s,a)=>(Ot(t,e,"write to private field"),a?a.call(t,s):e.set(t,s),s),C=(t,e,s)=>(Ot(t,e,"access private method"),s);var kt=(t,e,s,a)=>({set _(i){d(t,e,i,s)},get _(){return r(t,e,a)}});import{r as m,a as Ra,u as Oa}from"./vendor-http-CF_ya5s8.js";var Pt={exports:{}},tt={};/**
 * @license React
 * react-jsx-runtime.production.js
 *
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 *
 * This source code is licensed under the MIT license found in the
 * LICENSE file in the root directory of this source tree.
 */var gs;function Pa(){if(gs)return tt;gs=1;var t=Symbol.for("react.transitional.element"),e=Symbol.for("react.fragment");function s(a,i,n){var o=null;if(n!==void 0&&(o=""+n),i.key!==void 0&&(o=""+i.key),"key"in i){n={};for(var l in i)l!=="key"&&(n[l]=i[l])}else n=i;return i=n.ref,{$$typeof:t,type:a,key:o,ref:i!==void 0?i:null,props:n}}return tt.Fragment=e,tt.jsx=s,tt.jsxs=s,tt}var xs;function $a(){return xs||(xs=1,Pt.exports=Pa()),Pt.exports}var c=$a(),De=class{constructor(){this.listeners=new Set,this.subscribe=this.subscribe.bind(this)}subscribe(t){return this.listeners.add(t),this.onSubscribe(),()=>{this.listeners.delete(t),this.onUnsubscribe()}}hasListeners(){return this.listeners.size>0}onSubscribe(){}onUnsubscribe(){}},_e,ye,Qe,Ds,Ta=(Ds=class extends De{constructor(){super();x(this,_e);x(this,ye);x(this,Qe);d(this,Qe,e=>{if(typeof window<"u"&&window.addEventListener){const s=()=>e();return window.addEventListener("visibilitychange",s,!1),()=>{window.removeEventListener("visibilitychange",s)}}})}onSubscribe(){r(this,ye)||this.setEventListener(r(this,Qe))}onUnsubscribe(){var e;this.hasListeners()||((e=r(this,ye))==null||e.call(this),d(this,ye,void 0))}setEventListener(e){var s;d(this,Qe,e),(s=r(this,ye))==null||s.call(this),d(this,ye,e(a=>{typeof a=="boolean"?this.setFocused(a):this.onFocus()}))}setFocused(e){r(this,_e)!==e&&(d(this,_e,e),this.onFocus())}onFocus(){const e=this.isFocused();this.listeners.forEach(s=>{s(e)})}isFocused(){var e;return typeof r(this,_e)=="boolean"?r(this,_e):((e=globalThis.document)==null?void 0:e.visibilityState)!=="hidden"}},_e=new WeakMap,ye=new WeakMap,Qe=new WeakMap,Ds),is=new Ta,Aa={setTimeout:(t,e)=>setTimeout(t,e),clearTimeout:t=>clearTimeout(t),setInterval:(t,e)=>setInterval(t,e),clearInterval:t=>clearInterval(t)},me,rs,qs,Ia=(qs=class{constructor(){x(this,me,Aa);x(this,rs,!1)}setTimeoutProvider(t){d(this,me,t)}setTimeout(t,e){return r(this,me).setTimeout(t,e)}clearTimeout(t){r(this,me).clearTimeout(t)}setInterval(t,e){return r(this,me).setInterval(t,e)}clearInterval(t){r(this,me).clearInterval(t)}},me=new WeakMap,rs=new WeakMap,qs),Se=new Ia;function Fa(t){setTimeout(t,0)}var Da=typeof window>"u"||"Deno"in globalThis;function L(){}function qa(t,e){return typeof t=="function"?t(e):t}function Tt(t){return typeof t=="number"&&t>=0&&t!==1/0}function Zs(t,e){return Math.max(t+(e||0)-Date.now(),0)}function je(t,e){return typeof t=="function"?t(e):t}function Z(t,e){return typeof t=="function"?t(e):t}function vs(t,e){const{type:s="all",exact:a,fetchStatus:i,predicate:n,queryKey:o,stale:l}=t;if(o){if(a){if(e.queryHash!==ns(o,e.options))return!1}else if(!rt(e.queryKey,o))return!1}if(s!=="all"){const u=e.isActive();if(s==="active"&&!u||s==="inactive"&&u)return!1}return!(typeof l=="boolean"&&e.isStale()!==l||i&&i!==e.state.fetchStatus||n&&!n(e))}function bs(t,e){const{exact:s,status:a,predicate:i,mutationKey:n}=t;if(n){if(!e.options.mutationKey)return!1;if(s){if(Fe(e.options.mutationKey)!==Fe(n))return!1}else if(!rt(e.options.mutationKey,n))return!1}return!(a&&e.state.status!==a||i&&!i(e))}function ns(t,e){return((e==null?void 0:e.queryKeyHashFn)||Fe)(t)}function Fe(t){return JSON.stringify(t,(e,s)=>At(s)?Object.keys(s).sort().reduce((a,i)=>(a[i]=s[i],a),{}):s)}function rt(t,e){return t===e?!0:typeof t!=typeof e?!1:t&&e&&typeof t=="object"&&typeof e=="object"?Object.keys(e).every(s=>rt(t[s],e[s])):!1}var Qa=Object.prototype.hasOwnProperty;function os(t,e,s=0){if(t===e)return t;if(s>500)return e;const a=ks(t)&&ks(e);if(!a&&!(At(t)&&At(e)))return e;const n=(a?t:Object.keys(t)).length,o=a?e:Object.keys(e),l=o.length,u=a?new Array(l):{};let f=0;for(let y=0;y<l;y++){const h=a?y:o[y],b=t[h],g=e[h];if(b===g){u[h]=b,(a?y<n:Qa.call(t,h))&&f++;continue}if(b===null||g===null||typeof b!="object"||typeof g!="object"){u[h]=g;continue}const N=os(b,g,s+1);u[h]=N,N===b&&f++}return n===l&&f===n?t:u}function it(t,e){if(!e||Object.keys(t).length!==Object.keys(e).length)return!1;for(const s in t)if(t[s]!==e[s])return!1;return!0}function ks(t){return Array.isArray(t)&&t.length===Object.keys(t).length}function At(t){if(!ws(t))return!1;const e=t.constructor;if(e===void 0)return!0;const s=e.prototype;return!(!ws(s)||!s.hasOwnProperty("isPrototypeOf")||Object.getPrototypeOf(t)!==Object.prototype)}function ws(t){return Object.prototype.toString.call(t)==="[object Object]"}function La(t){return new Promise(e=>{Se.setTimeout(e,t)})}function It(t,e,s){return typeof s.structuralSharing=="function"?s.structuralSharing(t,e):s.structuralSharing!==!1?os(t,e):e}function za(t,e,s=0){const a=[...t,e];return s&&a.length>s?a.slice(1):a}function Ua(t,e,s=0){const a=[e,...t];return s&&a.length>s?a.slice(0,-1):a}var cs=Symbol();function Ws(t,e){return!t.queryFn&&(e!=null&&e.initialPromise)?()=>e.initialPromise:!t.queryFn||t.queryFn===cs?()=>Promise.reject(new Error(`Missing queryFn: '${t.queryHash}'`)):t.queryFn}function ls(t,e){return typeof t=="function"?t(...e):!!t}function Ha(t,e,s){let a=!1,i;return Object.defineProperty(t,"signal",{enumerable:!0,get:()=>(i??(i=e()),a||(a=!0,i.aborted?s():i.addEventListener("abort",s,{once:!0})),i)}),t}var nt=(()=>{let t=()=>Da;return{isServer(){return t()},setIsServer(e){t=e}}})();function Ft(){let t,e;const s=new Promise((i,n)=>{t=i,e=n});s.status="pending",s.catch(()=>{});function a(i){Object.assign(s,i),delete s.resolve,delete s.reject}return s.resolve=i=>{a({status:"fulfilled",value:i}),t(i)},s.reject=i=>{a({status:"rejected",reason:i}),e(i)},s}var Ka=Fa;function Va(){let t=[],e=0,s=l=>{l()},a=l=>{l()},i=Ka;const n=l=>{e?t.push(l):i(()=>{s(l)})},o=()=>{const l=t;t=[],l.length&&i(()=>{a(()=>{l.forEach(u=>{s(u)})})})};return{batch:l=>{let u;e++;try{u=l()}finally{e--,e||o()}return u},batchCalls:l=>(...u)=>{n(()=>{l(...u)})},schedule:n,setNotifyFunction:l=>{s=l},setBatchNotifyFunction:l=>{a=l},setScheduler:l=>{i=l}}}var T=Va(),Le,ge,ze,Qs,Ba=(Qs=class extends De{constructor(){super();x(this,Le,!0);x(this,ge);x(this,ze);d(this,ze,e=>{if(typeof window<"u"&&window.addEventListener){const s=()=>e(!0),a=()=>e(!1);return window.addEventListener("online",s,!1),window.addEventListener("offline",a,!1),()=>{window.removeEventListener("online",s),window.removeEventListener("offline",a)}}})}onSubscribe(){r(this,ge)||this.setEventListener(r(this,ze))}onUnsubscribe(){var e;this.hasListeners()||((e=r(this,ge))==null||e.call(this),d(this,ge,void 0))}setEventListener(e){var s;d(this,ze,e),(s=r(this,ge))==null||s.call(this),d(this,ge,e(this.setOnline.bind(this)))}setOnline(e){r(this,Le)!==e&&(d(this,Le,e),this.listeners.forEach(a=>{a(e)}))}isOnline(){return r(this,Le)}},Le=new WeakMap,ge=new WeakMap,ze=new WeakMap,Qs),Nt=new Ba;function Ga(t){return Math.min(1e3*2**t,3e4)}function Ys(t){return(t??"online")==="online"?Nt.isOnline():!0}var Dt=class extends Error{constructor(t){super("CancelledError"),this.revert=t==null?void 0:t.revert,this.silent=t==null?void 0:t.silent}};function Xs(t){let e=!1,s=0,a;const i=Ft(),n=()=>i.status!=="pending",o=w=>{var j;if(!n()){const S=new Dt(w);b(S),(j=t.onCancel)==null||j.call(t,S)}},l=()=>{e=!0},u=()=>{e=!1},f=()=>is.isFocused()&&(t.networkMode==="always"||Nt.isOnline())&&t.canRun(),y=()=>Ys(t.networkMode)&&t.canRun(),h=w=>{n()||(a==null||a(),i.resolve(w))},b=w=>{n()||(a==null||a(),i.reject(w))},g=()=>new Promise(w=>{var j;a=S=>{(n()||f())&&w(S)},(j=t.onPause)==null||j.call(t)}).then(()=>{var w;a=void 0,n()||(w=t.onContinue)==null||w.call(t)}),N=()=>{if(n())return;let w;const j=s===0?t.initialPromise:void 0;try{w=j??t.fn()}catch(S){w=Promise.reject(S)}Promise.resolve(w).then(h).catch(S=>{var P;if(n())return;const O=t.retry??(nt.isServer()?0:3),k=t.retryDelay??Ga,M=typeof k=="function"?k(s,S):k,I=O===!0||typeof O=="number"&&s<O||typeof O=="function"&&O(s,S);if(e||!I){b(S);return}s++,(P=t.onFail)==null||P.call(t,s,S),La(M).then(()=>f()?void 0:g()).then(()=>{e?b(S):N()})})};return{promise:i,status:()=>i.status,cancel:o,continue:()=>(a==null||a(),i),cancelRetry:l,continueRetry:u,canStart:y,start:()=>(y()?N():g().then(N),i)}}var Ee,Ls,ea=(Ls=class{constructor(){x(this,Ee)}destroy(){this.clearGcTimeout()}scheduleGc(){this.clearGcTimeout(),Tt(this.gcTime)&&d(this,Ee,Se.setTimeout(()=>{this.optionalRemove()},this.gcTime))}updateGcTime(t){this.gcTime=Math.max(this.gcTime||0,t??(nt.isServer()?1/0:300*1e3))}clearGcTimeout(){r(this,Ee)&&(Se.clearTimeout(r(this,Ee)),d(this,Ee,void 0))}},Ee=new WeakMap,Ls),Re,Ue,G,Oe,D,ot,Pe,B,ta,re,zs,Ja=(zs=class extends ea{constructor(e){super();x(this,B);x(this,Re);x(this,Ue);x(this,G);x(this,Oe);x(this,D);x(this,ot);x(this,Pe);d(this,Pe,!1),d(this,ot,e.defaultOptions),this.setOptions(e.options),this.observers=[],d(this,Oe,e.client),d(this,G,r(this,Oe).getQueryCache()),this.queryKey=e.queryKey,this.queryHash=e.queryHash,d(this,Re,Cs(this.options)),this.state=e.state??r(this,Re),this.scheduleGc()}get meta(){return this.options.meta}get promise(){var e;return(e=r(this,D))==null?void 0:e.promise}setOptions(e){if(this.options={...r(this,ot),...e},this.updateGcTime(this.options.gcTime),this.state&&this.state.data===void 0){const s=Cs(this.options);s.data!==void 0&&(this.setState(Ms(s.data,s.dataUpdatedAt)),d(this,Re,s))}}optionalRemove(){!this.observers.length&&this.state.fetchStatus==="idle"&&r(this,G).remove(this)}setData(e,s){const a=It(this.state.data,e,this.options);return C(this,B,re).call(this,{data:a,type:"success",dataUpdatedAt:s==null?void 0:s.updatedAt,manual:s==null?void 0:s.manual}),a}setState(e,s){C(this,B,re).call(this,{type:"setState",state:e,setStateOptions:s})}cancel(e){var a,i;const s=(a=r(this,D))==null?void 0:a.promise;return(i=r(this,D))==null||i.cancel(e),s?s.then(L).catch(L):Promise.resolve()}destroy(){super.destroy(),this.cancel({silent:!0})}get resetState(){return r(this,Re)}reset(){this.destroy(),this.setState(this.resetState)}isActive(){return this.observers.some(e=>Z(e.options.enabled,this)!==!1)}isDisabled(){return this.getObserversCount()>0?!this.isActive():this.options.queryFn===cs||!this.isFetched()}isFetched(){return this.state.dataUpdateCount+this.state.errorUpdateCount>0}isStatic(){return this.getObserversCount()>0?this.observers.some(e=>je(e.options.staleTime,this)==="static"):!1}isStale(){return this.getObserversCount()>0?this.observers.some(e=>e.getCurrentResult().isStale):this.state.data===void 0||this.state.isInvalidated}isStaleByTime(e=0){return this.state.data===void 0?!0:e==="static"?!1:this.state.isInvalidated?!0:!Zs(this.state.dataUpdatedAt,e)}onFocus(){var s;const e=this.observers.find(a=>a.shouldFetchOnWindowFocus());e==null||e.refetch({cancelRefetch:!1}),(s=r(this,D))==null||s.continue()}onOnline(){var s;const e=this.observers.find(a=>a.shouldFetchOnReconnect());e==null||e.refetch({cancelRefetch:!1}),(s=r(this,D))==null||s.continue()}addObserver(e){this.observers.includes(e)||(this.observers.push(e),this.clearGcTimeout(),r(this,G).notify({type:"observerAdded",query:this,observer:e}))}removeObserver(e){this.observers.includes(e)&&(this.observers=this.observers.filter(s=>s!==e),this.observers.length||(r(this,D)&&(r(this,Pe)||C(this,B,ta).call(this)?r(this,D).cancel({revert:!0}):r(this,D).cancelRetry()),this.scheduleGc()),r(this,G).notify({type:"observerRemoved",query:this,observer:e}))}getObserversCount(){return this.observers.length}invalidate(){this.state.isInvalidated||C(this,B,re).call(this,{type:"invalidate"})}async fetch(e,s){var u,f,y,h,b,g,N,w,j,S,O,k;if(this.state.fetchStatus!=="idle"&&((u=r(this,D))==null?void 0:u.status())!=="rejected"){if(this.state.data!==void 0&&(s!=null&&s.cancelRefetch))this.cancel({silent:!0});else if(r(this,D))return r(this,D).continueRetry(),r(this,D).promise}if(e&&this.setOptions(e),!this.options.queryFn){const M=this.observers.find(I=>I.options.queryFn);M&&this.setOptions(M.options)}const a=new AbortController,i=M=>{Object.defineProperty(M,"signal",{enumerable:!0,get:()=>(d(this,Pe,!0),a.signal)})},n=()=>{const M=Ws(this.options,s),P=(()=>{const K={client:r(this,Oe),queryKey:this.queryKey,meta:this.meta};return i(K),K})();return d(this,Pe,!1),this.options.persister?this.options.persister(M,P,this):M(P)},l=(()=>{const M={fetchOptions:s,options:this.options,queryKey:this.queryKey,client:r(this,Oe),state:this.state,fetchFn:n};return i(M),M})();(f=this.options.behavior)==null||f.onFetch(l,this),d(this,Ue,this.state),(this.state.fetchStatus==="idle"||this.state.fetchMeta!==((y=l.fetchOptions)==null?void 0:y.meta))&&C(this,B,re).call(this,{type:"fetch",meta:(h=l.fetchOptions)==null?void 0:h.meta}),d(this,D,Xs({initialPromise:s==null?void 0:s.initialPromise,fn:l.fetchFn,onCancel:M=>{M instanceof Dt&&M.revert&&this.setState({...r(this,Ue),fetchStatus:"idle"}),a.abort()},onFail:(M,I)=>{C(this,B,re).call(this,{type:"failed",failureCount:M,error:I})},onPause:()=>{C(this,B,re).call(this,{type:"pause"})},onContinue:()=>{C(this,B,re).call(this,{type:"continue"})},retry:l.options.retry,retryDelay:l.options.retryDelay,networkMode:l.options.networkMode,canRun:()=>!0}));try{const M=await r(this,D).start();if(M===void 0)throw new Error(`${this.queryHash} data is undefined`);return this.setData(M),(g=(b=r(this,G).config).onSuccess)==null||g.call(b,M,this),(w=(N=r(this,G).config).onSettled)==null||w.call(N,M,this.state.error,this),M}catch(M){if(M instanceof Dt){if(M.silent)return r(this,D).promise;if(M.revert){if(this.state.data===void 0)throw M;return this.state.data}}throw C(this,B,re).call(this,{type:"error",error:M}),(S=(j=r(this,G).config).onError)==null||S.call(j,M,this),(k=(O=r(this,G).config).onSettled)==null||k.call(O,this.state.data,M,this),M}finally{this.scheduleGc()}}},Re=new WeakMap,Ue=new WeakMap,G=new WeakMap,Oe=new WeakMap,D=new WeakMap,ot=new WeakMap,Pe=new WeakMap,B=new WeakSet,ta=function(){return this.state.fetchStatus==="paused"&&this.state.status==="pending"},re=function(e){const s=a=>{switch(e.type){case"failed":return{...a,fetchFailureCount:e.failureCount,fetchFailureReason:e.error};case"pause":return{...a,fetchStatus:"paused"};case"continue":return{...a,fetchStatus:"fetching"};case"fetch":return{...a,...sa(a.data,this.options),fetchMeta:e.meta??null};case"success":const i={...a,...Ms(e.data,e.dataUpdatedAt),dataUpdateCount:a.dataUpdateCount+1,...!e.manual&&{fetchStatus:"idle",fetchFailureCount:0,fetchFailureReason:null}};return d(this,Ue,e.manual?i:void 0),i;case"error":const n=e.error;return{...a,error:n,errorUpdateCount:a.errorUpdateCount+1,errorUpdatedAt:Date.now(),fetchFailureCount:a.fetchFailureCount+1,fetchFailureReason:n,fetchStatus:"idle",status:"error",isInvalidated:!0};case"invalidate":return{...a,isInvalidated:!0};case"setState":return{...a,...e.state}}};this.state=s(this.state),T.batch(()=>{this.observers.forEach(a=>{a.onQueryUpdate()}),r(this,G).notify({query:this,type:"updated",action:e})})},zs);function sa(t,e){return{fetchFailureCount:0,fetchFailureReason:null,fetchStatus:Ys(e.networkMode)?"fetching":"paused",...t===void 0&&{error:null,status:"pending"}}}function Ms(t,e){return{data:t,dataUpdatedAt:e??Date.now(),error:null,isInvalidated:!1,status:"success"}}function Cs(t){const e=typeof t.initialData=="function"?t.initialData():t.initialData,s=e!==void 0,a=s?typeof t.initialDataUpdatedAt=="function"?t.initialDataUpdatedAt():t.initialDataUpdatedAt:0;return{data:e,dataUpdateCount:0,dataUpdatedAt:s?a??Date.now():0,error:null,errorUpdateCount:0,errorUpdatedAt:0,fetchFailureCount:0,fetchFailureReason:null,fetchMeta:null,isInvalidated:!1,status:s?"success":"pending",fetchStatus:"idle"}}var U,_,ct,z,$e,He,ie,xe,lt,Ke,Ve,Te,Ae,ve,Be,E,at,qt,Qt,Lt,zt,Ut,Ht,Kt,aa,Us,hs=(Us=class extends De{constructor(e,s){super();x(this,E);x(this,U);x(this,_);x(this,ct);x(this,z);x(this,$e);x(this,He);x(this,ie);x(this,xe);x(this,lt);x(this,Ke);x(this,Ve);x(this,Te);x(this,Ae);x(this,ve);x(this,Be,new Set);this.options=s,d(this,U,e),d(this,xe,null),d(this,ie,Ft()),this.bindMethods(),this.setOptions(s)}bindMethods(){this.refetch=this.refetch.bind(this)}onSubscribe(){this.listeners.size===1&&(r(this,_).addObserver(this),js(r(this,_),this.options)?C(this,E,at).call(this):this.updateResult(),C(this,E,zt).call(this))}onUnsubscribe(){this.hasListeners()||this.destroy()}shouldFetchOnReconnect(){return Vt(r(this,_),this.options,this.options.refetchOnReconnect)}shouldFetchOnWindowFocus(){return Vt(r(this,_),this.options,this.options.refetchOnWindowFocus)}destroy(){this.listeners=new Set,C(this,E,Ut).call(this),C(this,E,Ht).call(this),r(this,_).removeObserver(this)}setOptions(e){const s=this.options,a=r(this,_);if(this.options=r(this,U).defaultQueryOptions(e),this.options.enabled!==void 0&&typeof this.options.enabled!="boolean"&&typeof this.options.enabled!="function"&&typeof Z(this.options.enabled,r(this,_))!="boolean")throw new Error("Expected enabled to be a boolean or a callback that returns a boolean");C(this,E,Kt).call(this),r(this,_).setOptions(this.options),s._defaulted&&!it(this.options,s)&&r(this,U).getQueryCache().notify({type:"observerOptionsUpdated",query:r(this,_),observer:this});const i=this.hasListeners();i&&Ns(r(this,_),a,this.options,s)&&C(this,E,at).call(this),this.updateResult(),i&&(r(this,_)!==a||Z(this.options.enabled,r(this,_))!==Z(s.enabled,r(this,_))||je(this.options.staleTime,r(this,_))!==je(s.staleTime,r(this,_)))&&C(this,E,qt).call(this);const n=C(this,E,Qt).call(this);i&&(r(this,_)!==a||Z(this.options.enabled,r(this,_))!==Z(s.enabled,r(this,_))||n!==r(this,ve))&&C(this,E,Lt).call(this,n)}getOptimisticResult(e){const s=r(this,U).getQueryCache().build(r(this,U),e),a=this.createResult(s,e);return Wa(this,a)&&(d(this,z,a),d(this,He,this.options),d(this,$e,r(this,_).state)),a}getCurrentResult(){return r(this,z)}trackResult(e,s){return new Proxy(e,{get:(a,i)=>(this.trackProp(i),s==null||s(i),i==="promise"&&(this.trackProp("data"),!this.options.experimental_prefetchInRender&&r(this,ie).status==="pending"&&r(this,ie).reject(new Error("experimental_prefetchInRender feature flag is not enabled"))),Reflect.get(a,i))})}trackProp(e){r(this,Be).add(e)}getCurrentQuery(){return r(this,_)}refetch({...e}={}){return this.fetch({...e})}fetchOptimistic(e){const s=r(this,U).defaultQueryOptions(e),a=r(this,U).getQueryCache().build(r(this,U),s);return a.fetch().then(()=>this.createResult(a,s))}fetch(e){return C(this,E,at).call(this,{...e,cancelRefetch:e.cancelRefetch??!0}).then(()=>(this.updateResult(),r(this,z)))}createResult(e,s){var R;const a=r(this,_),i=this.options,n=r(this,z),o=r(this,$e),l=r(this,He),f=e!==a?e.state:r(this,ct),{state:y}=e;let h={...y},b=!1,g;if(s._optimisticResults){const A=this.hasListeners(),de=!A&&js(e,s),xt=A&&Ns(e,a,s,i);(de||xt)&&(h={...h,...sa(y.data,e.options)}),s._optimisticResults==="isRestoring"&&(h.fetchStatus="idle")}let{error:N,errorUpdatedAt:w,status:j}=h;g=h.data;let S=!1;if(s.placeholderData!==void 0&&g===void 0&&j==="pending"){let A;n!=null&&n.isPlaceholderData&&s.placeholderData===(l==null?void 0:l.placeholderData)?(A=n.data,S=!0):A=typeof s.placeholderData=="function"?s.placeholderData((R=r(this,Ve))==null?void 0:R.state.data,r(this,Ve)):s.placeholderData,A!==void 0&&(j="success",g=It(n==null?void 0:n.data,A,s),b=!0)}if(s.select&&g!==void 0&&!S)if(n&&g===(o==null?void 0:o.data)&&s.select===r(this,lt))g=r(this,Ke);else try{d(this,lt,s.select),g=s.select(g),g=It(n==null?void 0:n.data,g,s),d(this,Ke,g),d(this,xe,null)}catch(A){d(this,xe,A)}r(this,xe)&&(N=r(this,xe),g=r(this,Ke),w=Date.now(),j="error");const O=h.fetchStatus==="fetching",k=j==="pending",M=j==="error",I=k&&O,P=g!==void 0,v={status:j,fetchStatus:h.fetchStatus,isPending:k,isSuccess:j==="success",isError:M,isInitialLoading:I,isLoading:I,data:g,dataUpdatedAt:h.dataUpdatedAt,error:N,errorUpdatedAt:w,failureCount:h.fetchFailureCount,failureReason:h.fetchFailureReason,errorUpdateCount:h.errorUpdateCount,isFetched:e.isFetched(),isFetchedAfterMount:h.dataUpdateCount>f.dataUpdateCount||h.errorUpdateCount>f.errorUpdateCount,isFetching:O,isRefetching:O&&!k,isLoadingError:M&&!P,isPaused:h.fetchStatus==="paused",isPlaceholderData:b,isRefetchError:M&&P,isStale:us(e,s),refetch:this.refetch,promise:r(this,ie),isEnabled:Z(s.enabled,e)!==!1};if(this.options.experimental_prefetchInRender){const A=v.data!==void 0,de=v.status==="error"&&!A,xt=bt=>{de?bt.reject(v.error):A&&bt.resolve(v.data)},ys=()=>{const bt=d(this,ie,v.promise=Ft());xt(bt)},vt=r(this,ie);switch(vt.status){case"pending":e.queryHash===a.queryHash&&xt(vt);break;case"fulfilled":(de||v.data!==vt.value)&&ys();break;case"rejected":(!de||v.error!==vt.reason)&&ys();break}}return v}updateResult(){const e=r(this,z),s=this.createResult(r(this,_),this.options);if(d(this,$e,r(this,_).state),d(this,He,this.options),r(this,$e).data!==void 0&&d(this,Ve,r(this,_)),it(s,e))return;d(this,z,s);const a=()=>{if(!e)return!0;const{notifyOnChangeProps:i}=this.options,n=typeof i=="function"?i():i;if(n==="all"||!n&&!r(this,Be).size)return!0;const o=new Set(n??r(this,Be));return this.options.throwOnError&&o.add("error"),Object.keys(r(this,z)).some(l=>{const u=l;return r(this,z)[u]!==e[u]&&o.has(u)})};C(this,E,aa).call(this,{listeners:a()})}onQueryUpdate(){this.updateResult(),this.hasListeners()&&C(this,E,zt).call(this)}},U=new WeakMap,_=new WeakMap,ct=new WeakMap,z=new WeakMap,$e=new WeakMap,He=new WeakMap,ie=new WeakMap,xe=new WeakMap,lt=new WeakMap,Ke=new WeakMap,Ve=new WeakMap,Te=new WeakMap,Ae=new WeakMap,ve=new WeakMap,Be=new WeakMap,E=new WeakSet,at=function(e){C(this,E,Kt).call(this);let s=r(this,_).fetch(this.options,e);return e!=null&&e.throwOnError||(s=s.catch(L)),s},qt=function(){C(this,E,Ut).call(this);const e=je(this.options.staleTime,r(this,_));if(nt.isServer()||r(this,z).isStale||!Tt(e))return;const a=Zs(r(this,z).dataUpdatedAt,e)+1;d(this,Te,Se.setTimeout(()=>{r(this,z).isStale||this.updateResult()},a))},Qt=function(){return(typeof this.options.refetchInterval=="function"?this.options.refetchInterval(r(this,_)):this.options.refetchInterval)??!1},Lt=function(e){C(this,E,Ht).call(this),d(this,ve,e),!(nt.isServer()||Z(this.options.enabled,r(this,_))===!1||!Tt(r(this,ve))||r(this,ve)===0)&&d(this,Ae,Se.setInterval(()=>{(this.options.refetchIntervalInBackground||is.isFocused())&&C(this,E,at).call(this)},r(this,ve)))},zt=function(){C(this,E,qt).call(this),C(this,E,Lt).call(this,C(this,E,Qt).call(this))},Ut=function(){r(this,Te)&&(Se.clearTimeout(r(this,Te)),d(this,Te,void 0))},Ht=function(){r(this,Ae)&&(Se.clearInterval(r(this,Ae)),d(this,Ae,void 0))},Kt=function(){const e=r(this,U).getQueryCache().build(r(this,U),this.options);if(e===r(this,_))return;const s=r(this,_);d(this,_,e),d(this,ct,e.state),this.hasListeners()&&(s==null||s.removeObserver(this),e.addObserver(this))},aa=function(e){T.batch(()=>{e.listeners&&this.listeners.forEach(s=>{s(r(this,z))}),r(this,U).getQueryCache().notify({query:r(this,_),type:"observerResultsUpdated"})})},Us);function Za(t,e){return Z(e.enabled,t)!==!1&&t.state.data===void 0&&!(t.state.status==="error"&&e.retryOnMount===!1)}function js(t,e){return Za(t,e)||t.state.data!==void 0&&Vt(t,e,e.refetchOnMount)}function Vt(t,e,s){if(Z(e.enabled,t)!==!1&&je(e.staleTime,t)!=="static"){const a=typeof s=="function"?s(t):s;return a==="always"||a!==!1&&us(t,e)}return!1}function Ns(t,e,s,a){return(t!==e||Z(a.enabled,t)===!1)&&(!s.suspense||t.state.status!=="error")&&us(t,s)}function us(t,e){return Z(e.enabled,t)!==!1&&t.isStaleByTime(je(e.staleTime,t))}function Wa(t,e){return!it(t.getCurrentResult(),e)}function Ss(t){return{onFetch:(e,s)=>{var y,h,b,g,N;const a=e.options,i=(b=(h=(y=e.fetchOptions)==null?void 0:y.meta)==null?void 0:h.fetchMore)==null?void 0:b.direction,n=((g=e.state.data)==null?void 0:g.pages)||[],o=((N=e.state.data)==null?void 0:N.pageParams)||[];let l={pages:[],pageParams:[]},u=0;const f=async()=>{let w=!1;const j=k=>{Ha(k,()=>e.signal,()=>w=!0)},S=Ws(e.options,e.fetchOptions),O=async(k,M,I)=>{if(w)return Promise.reject();if(M==null&&k.pages.length)return Promise.resolve(k);const K=(()=>{const de={client:e.client,queryKey:e.queryKey,pageParam:M,direction:I?"backward":"forward",meta:e.options.meta};return j(de),de})(),v=await S(K),{maxPages:R}=e.options,A=I?Ua:za;return{pages:A(k.pages,v,R),pageParams:A(k.pageParams,M,R)}};if(i&&n.length){const k=i==="backward",M=k?Ya:_s,I={pages:n,pageParams:o},P=M(a,I);l=await O(I,P,k)}else{const k=t??n.length;do{const M=u===0?o[0]??a.initialPageParam:_s(a,l);if(u>0&&M==null)break;l=await O(l,M),u++}while(u<k)}return l};e.options.persister?e.fetchFn=()=>{var w,j;return(j=(w=e.options).persister)==null?void 0:j.call(w,f,{client:e.client,queryKey:e.queryKey,meta:e.options.meta,signal:e.signal},s)}:e.fetchFn=f}}}function _s(t,{pages:e,pageParams:s}){const a=e.length-1;return e.length>0?t.getNextPageParam(e[a],e,s[a],s):void 0}function Ya(t,{pages:e,pageParams:s}){var a;return e.length>0?(a=t.getPreviousPageParam)==null?void 0:a.call(t,e[0],e,s[0],s):void 0}var ht,X,Q,Ie,ee,pe,Hs,Xa=(Hs=class extends ea{constructor(e){super();x(this,ee);x(this,ht);x(this,X);x(this,Q);x(this,Ie);d(this,ht,e.client),this.mutationId=e.mutationId,d(this,Q,e.mutationCache),d(this,X,[]),this.state=e.state||ra(),this.setOptions(e.options),this.scheduleGc()}setOptions(e){this.options=e,this.updateGcTime(this.options.gcTime)}get meta(){return this.options.meta}addObserver(e){r(this,X).includes(e)||(r(this,X).push(e),this.clearGcTimeout(),r(this,Q).notify({type:"observerAdded",mutation:this,observer:e}))}removeObserver(e){d(this,X,r(this,X).filter(s=>s!==e)),this.scheduleGc(),r(this,Q).notify({type:"observerRemoved",mutation:this,observer:e})}optionalRemove(){r(this,X).length||(this.state.status==="pending"?this.scheduleGc():r(this,Q).remove(this))}continue(){var e;return((e=r(this,Ie))==null?void 0:e.continue())??this.execute(this.state.variables)}async execute(e){var o,l,u,f,y,h,b,g,N,w,j,S,O,k,M,I,P,K;const s=()=>{C(this,ee,pe).call(this,{type:"continue"})},a={client:r(this,ht),meta:this.options.meta,mutationKey:this.options.mutationKey};d(this,Ie,Xs({fn:()=>this.options.mutationFn?this.options.mutationFn(e,a):Promise.reject(new Error("No mutationFn found")),onFail:(v,R)=>{C(this,ee,pe).call(this,{type:"failed",failureCount:v,error:R})},onPause:()=>{C(this,ee,pe).call(this,{type:"pause"})},onContinue:s,retry:this.options.retry??0,retryDelay:this.options.retryDelay,networkMode:this.options.networkMode,canRun:()=>r(this,Q).canRun(this)}));const i=this.state.status==="pending",n=!r(this,Ie).canStart();try{if(i)s();else{C(this,ee,pe).call(this,{type:"pending",variables:e,isPaused:n}),r(this,Q).config.onMutate&&await r(this,Q).config.onMutate(e,this,a);const R=await((l=(o=this.options).onMutate)==null?void 0:l.call(o,e,a));R!==this.state.context&&C(this,ee,pe).call(this,{type:"pending",context:R,variables:e,isPaused:n})}const v=await r(this,Ie).start();return await((f=(u=r(this,Q).config).onSuccess)==null?void 0:f.call(u,v,e,this.state.context,this,a)),await((h=(y=this.options).onSuccess)==null?void 0:h.call(y,v,e,this.state.context,a)),await((g=(b=r(this,Q).config).onSettled)==null?void 0:g.call(b,v,null,this.state.variables,this.state.context,this,a)),await((w=(N=this.options).onSettled)==null?void 0:w.call(N,v,null,e,this.state.context,a)),C(this,ee,pe).call(this,{type:"success",data:v}),v}catch(v){try{await((S=(j=r(this,Q).config).onError)==null?void 0:S.call(j,v,e,this.state.context,this,a))}catch(R){Promise.reject(R)}try{await((k=(O=this.options).onError)==null?void 0:k.call(O,v,e,this.state.context,a))}catch(R){Promise.reject(R)}try{await((I=(M=r(this,Q).config).onSettled)==null?void 0:I.call(M,void 0,v,this.state.variables,this.state.context,this,a))}catch(R){Promise.reject(R)}try{await((K=(P=this.options).onSettled)==null?void 0:K.call(P,void 0,v,e,this.state.context,a))}catch(R){Promise.reject(R)}throw C(this,ee,pe).call(this,{type:"error",error:v}),v}finally{r(this,Q).runNext(this)}}},ht=new WeakMap,X=new WeakMap,Q=new WeakMap,Ie=new WeakMap,ee=new WeakSet,pe=function(e){const s=a=>{switch(e.type){case"failed":return{...a,failureCount:e.failureCount,failureReason:e.error};case"pause":return{...a,isPaused:!0};case"continue":return{...a,isPaused:!1};case"pending":return{...a,context:e.context,data:void 0,failureCount:0,failureReason:null,error:null,isPaused:e.isPaused,status:"pending",variables:e.variables,submittedAt:Date.now()};case"success":return{...a,data:e.data,failureCount:0,failureReason:null,error:null,status:"success",isPaused:!1};case"error":return{...a,data:void 0,error:e.error,failureCount:a.failureCount+1,failureReason:e.error,isPaused:!1,status:"error"}}};this.state=s(this.state),T.batch(()=>{r(this,X).forEach(a=>{a.onMutationUpdate(e)}),r(this,Q).notify({mutation:this,type:"updated",action:e})})},Hs);function ra(){return{context:void 0,data:void 0,error:null,failureCount:0,failureReason:null,isPaused:!1,status:"idle",variables:void 0,submittedAt:0}}var ne,W,ut,Ks,er=(Ks=class extends De{constructor(e={}){super();x(this,ne);x(this,W);x(this,ut);this.config=e,d(this,ne,new Set),d(this,W,new Map),d(this,ut,0)}build(e,s,a){const i=new Xa({client:e,mutationCache:this,mutationId:++kt(this,ut)._,options:e.defaultMutationOptions(s),state:a});return this.add(i),i}add(e){r(this,ne).add(e);const s=wt(e);if(typeof s=="string"){const a=r(this,W).get(s);a?a.push(e):r(this,W).set(s,[e])}this.notify({type:"added",mutation:e})}remove(e){if(r(this,ne).delete(e)){const s=wt(e);if(typeof s=="string"){const a=r(this,W).get(s);if(a)if(a.length>1){const i=a.indexOf(e);i!==-1&&a.splice(i,1)}else a[0]===e&&r(this,W).delete(s)}}this.notify({type:"removed",mutation:e})}canRun(e){const s=wt(e);if(typeof s=="string"){const a=r(this,W).get(s),i=a==null?void 0:a.find(n=>n.state.status==="pending");return!i||i===e}else return!0}runNext(e){var a;const s=wt(e);if(typeof s=="string"){const i=(a=r(this,W).get(s))==null?void 0:a.find(n=>n!==e&&n.state.isPaused);return(i==null?void 0:i.continue())??Promise.resolve()}else return Promise.resolve()}clear(){T.batch(()=>{r(this,ne).forEach(e=>{this.notify({type:"removed",mutation:e})}),r(this,ne).clear(),r(this,W).clear()})}getAll(){return Array.from(r(this,ne))}find(e){const s={exact:!0,...e};return this.getAll().find(a=>bs(s,a))}findAll(e={}){return this.getAll().filter(s=>bs(e,s))}notify(e){T.batch(()=>{this.listeners.forEach(s=>{s(e)})})}resumePausedMutations(){const e=this.getAll().filter(s=>s.state.isPaused);return T.batch(()=>Promise.all(e.map(s=>s.continue().catch(L))))}},ne=new WeakMap,W=new WeakMap,ut=new WeakMap,Ks);function wt(t){var e;return(e=t.options.scope)==null?void 0:e.id}var oe,be,H,ce,he,Ct,Bt,Vs,tr=(Vs=class extends De{constructor(s,a){super();x(this,he);x(this,oe);x(this,be);x(this,H);x(this,ce);d(this,oe,s),this.setOptions(a),this.bindMethods(),C(this,he,Ct).call(this)}bindMethods(){this.mutate=this.mutate.bind(this),this.reset=this.reset.bind(this)}setOptions(s){var i;const a=this.options;this.options=r(this,oe).defaultMutationOptions(s),it(this.options,a)||r(this,oe).getMutationCache().notify({type:"observerOptionsUpdated",mutation:r(this,H),observer:this}),a!=null&&a.mutationKey&&this.options.mutationKey&&Fe(a.mutationKey)!==Fe(this.options.mutationKey)?this.reset():((i=r(this,H))==null?void 0:i.state.status)==="pending"&&r(this,H).setOptions(this.options)}onUnsubscribe(){var s;this.hasListeners()||(s=r(this,H))==null||s.removeObserver(this)}onMutationUpdate(s){C(this,he,Ct).call(this),C(this,he,Bt).call(this,s)}getCurrentResult(){return r(this,be)}reset(){var s;(s=r(this,H))==null||s.removeObserver(this),d(this,H,void 0),C(this,he,Ct).call(this),C(this,he,Bt).call(this)}mutate(s,a){var i;return d(this,ce,a),(i=r(this,H))==null||i.removeObserver(this),d(this,H,r(this,oe).getMutationCache().build(r(this,oe),this.options)),r(this,H).addObserver(this),r(this,H).execute(s)}},oe=new WeakMap,be=new WeakMap,H=new WeakMap,ce=new WeakMap,he=new WeakSet,Ct=function(){var a;const s=((a=r(this,H))==null?void 0:a.state)??ra();d(this,be,{...s,isPending:s.status==="pending",isSuccess:s.status==="success",isError:s.status==="error",isIdle:s.status==="idle",mutate:this.mutate,reset:this.reset})},Bt=function(s){T.batch(()=>{var a,i,n,o,l,u,f,y;if(r(this,ce)&&this.hasListeners()){const h=r(this,be).variables,b=r(this,be).context,g={client:r(this,oe),meta:this.options.meta,mutationKey:this.options.mutationKey};if((s==null?void 0:s.type)==="success"){try{(i=(a=r(this,ce)).onSuccess)==null||i.call(a,s.data,h,b,g)}catch(N){Promise.reject(N)}try{(o=(n=r(this,ce)).onSettled)==null||o.call(n,s.data,null,h,b,g)}catch(N){Promise.reject(N)}}else if((s==null?void 0:s.type)==="error"){try{(u=(l=r(this,ce)).onError)==null||u.call(l,s.error,h,b,g)}catch(N){Promise.reject(N)}try{(y=(f=r(this,ce)).onSettled)==null||y.call(f,void 0,s.error,h,b,g)}catch(N){Promise.reject(N)}}}this.listeners.forEach(h=>{h(r(this,be))})})},Vs);function Es(t,e){const s=new Set(e);return t.filter(a=>!s.has(a))}function sr(t,e,s){const a=t.slice(0);return a[e]=s,a}var Ge,V,Je,Ze,J,ke,dt,pt,ft,yt,q,Gt,Jt,Zt,Wt,Yt,Bs,ar=(Bs=class extends De{constructor(e,s,a){super();x(this,q);x(this,Ge);x(this,V);x(this,Je);x(this,Ze);x(this,J);x(this,ke);x(this,dt);x(this,pt);x(this,ft);x(this,yt,[]);d(this,Ge,e),d(this,Ze,a),d(this,Je,[]),d(this,J,[]),d(this,V,[]),this.setQueries(s)}onSubscribe(){this.listeners.size===1&&r(this,J).forEach(e=>{e.subscribe(s=>{C(this,q,Wt).call(this,e,s)})})}onUnsubscribe(){this.listeners.size||this.destroy()}destroy(){this.listeners=new Set,r(this,J).forEach(e=>{e.destroy()})}setQueries(e,s){d(this,Je,e),d(this,Ze,s),T.batch(()=>{const a=r(this,J),i=C(this,q,Zt).call(this,r(this,Je));i.forEach(h=>h.observer.setOptions(h.defaultedQueryOptions));const n=i.map(h=>h.observer),o=n.map(h=>h.getCurrentResult()),l=a.length!==n.length,u=n.some((h,b)=>h!==a[b]),f=l||u,y=f?!0:o.some((h,b)=>{const g=r(this,V)[b];return!g||!it(h,g)});!f&&!y||(f&&(d(this,yt,i),d(this,J,n)),d(this,V,o),this.hasListeners()&&(f&&(Es(a,n).forEach(h=>{h.destroy()}),Es(n,a).forEach(h=>{h.subscribe(b=>{C(this,q,Wt).call(this,h,b)})})),C(this,q,Yt).call(this)))})}getCurrentResult(){return r(this,V)}getQueries(){return r(this,J).map(e=>e.getCurrentQuery())}getObservers(){return r(this,J)}getOptimisticResult(e,s){const a=C(this,q,Zt).call(this,e),i=a.map(o=>o.observer.getOptimisticResult(o.defaultedQueryOptions)),n=a.map(o=>o.defaultedQueryOptions.queryHash);return[i,o=>C(this,q,Jt).call(this,o??i,s,n),()=>C(this,q,Gt).call(this,i,a)]}},Ge=new WeakMap,V=new WeakMap,Je=new WeakMap,Ze=new WeakMap,J=new WeakMap,ke=new WeakMap,dt=new WeakMap,pt=new WeakMap,ft=new WeakMap,yt=new WeakMap,q=new WeakSet,Gt=function(e,s){return s.map((a,i)=>{const n=e[i];return a.defaultedQueryOptions.notifyOnChangeProps?n:a.observer.trackResult(n,o=>{s.forEach(l=>{l.observer.trackProp(o)})})})},Jt=function(e,s,a){if(s){const i=r(this,ft),n=a!==void 0&&i!==void 0&&(i.length!==a.length||a.some((o,l)=>o!==i[l]));return(!r(this,ke)||r(this,V)!==r(this,pt)||n||s!==r(this,dt))&&(d(this,dt,s),d(this,pt,r(this,V)),a!==void 0&&d(this,ft,a),d(this,ke,os(r(this,ke),s(e)))),r(this,ke)}return e},Zt=function(e){const s=new Map;r(this,J).forEach(i=>{const n=i.options.queryHash;if(!n)return;const o=s.get(n);o?o.push(i):s.set(n,[i])});const a=[];return e.forEach(i=>{var u;const n=r(this,Ge).defaultQueryOptions(i),l=((u=s.get(n.queryHash))==null?void 0:u.shift())??new hs(r(this,Ge),n);a.push({defaultedQueryOptions:n,observer:l})}),a},Wt=function(e,s){const a=r(this,J).indexOf(e);a!==-1&&(d(this,V,sr(r(this,V),a,s)),C(this,q,Yt).call(this))},Yt=function(){var e;if(this.hasListeners()){const s=r(this,ke),a=C(this,q,Gt).call(this,r(this,V),r(this,yt)),i=C(this,q,Jt).call(this,a,(e=r(this,Ze))==null?void 0:e.combine);s!==i&&T.batch(()=>{this.listeners.forEach(n=>{n(r(this,V))})})}},Bs),te,Gs,rr=(Gs=class extends De{constructor(e={}){super();x(this,te);this.config=e,d(this,te,new Map)}build(e,s,a){const i=s.queryKey,n=s.queryHash??ns(i,s);let o=this.get(n);return o||(o=new Ja({client:e,queryKey:i,queryHash:n,options:e.defaultQueryOptions(s),state:a,defaultOptions:e.getQueryDefaults(i)}),this.add(o)),o}add(e){r(this,te).has(e.queryHash)||(r(this,te).set(e.queryHash,e),this.notify({type:"added",query:e}))}remove(e){const s=r(this,te).get(e.queryHash);s&&(e.destroy(),s===e&&r(this,te).delete(e.queryHash),this.notify({type:"removed",query:e}))}clear(){T.batch(()=>{this.getAll().forEach(e=>{this.remove(e)})})}get(e){return r(this,te).get(e)}getAll(){return[...r(this,te).values()]}find(e){const s={exact:!0,...e};return this.getAll().find(a=>vs(s,a))}findAll(e={}){const s=this.getAll();return Object.keys(e).length>0?s.filter(a=>vs(e,a)):s}notify(e){T.batch(()=>{this.listeners.forEach(s=>{s(e)})})}onFocus(){T.batch(()=>{this.getAll().forEach(e=>{e.onFocus()})})}onOnline(){T.batch(()=>{this.getAll().forEach(e=>{e.onOnline()})})}},te=new WeakMap,Gs),$,we,Me,We,Ye,Ce,Xe,et,Js,Xn=(Js=class{constructor(t={}){x(this,$);x(this,we);x(this,Me);x(this,We);x(this,Ye);x(this,Ce);x(this,Xe);x(this,et);d(this,$,t.queryCache||new rr),d(this,we,t.mutationCache||new er),d(this,Me,t.defaultOptions||{}),d(this,We,new Map),d(this,Ye,new Map),d(this,Ce,0)}mount(){kt(this,Ce)._++,r(this,Ce)===1&&(d(this,Xe,is.subscribe(async t=>{t&&(await this.resumePausedMutations(),r(this,$).onFocus())})),d(this,et,Nt.subscribe(async t=>{t&&(await this.resumePausedMutations(),r(this,$).onOnline())})))}unmount(){var t,e;kt(this,Ce)._--,r(this,Ce)===0&&((t=r(this,Xe))==null||t.call(this),d(this,Xe,void 0),(e=r(this,et))==null||e.call(this),d(this,et,void 0))}isFetching(t){return r(this,$).findAll({...t,fetchStatus:"fetching"}).length}isMutating(t){return r(this,we).findAll({...t,status:"pending"}).length}getQueryData(t){var s;const e=this.defaultQueryOptions({queryKey:t});return(s=r(this,$).get(e.queryHash))==null?void 0:s.state.data}ensureQueryData(t){const e=this.defaultQueryOptions(t),s=r(this,$).build(this,e),a=s.state.data;return a===void 0?this.fetchQuery(t):(t.revalidateIfStale&&s.isStaleByTime(je(e.staleTime,s))&&this.prefetchQuery(e),Promise.resolve(a))}getQueriesData(t){return r(this,$).findAll(t).map(({queryKey:e,state:s})=>{const a=s.data;return[e,a]})}setQueryData(t,e,s){const a=this.defaultQueryOptions({queryKey:t}),i=r(this,$).get(a.queryHash),n=i==null?void 0:i.state.data,o=qa(e,n);if(o!==void 0)return r(this,$).build(this,a).setData(o,{...s,manual:!0})}setQueriesData(t,e,s){return T.batch(()=>r(this,$).findAll(t).map(({queryKey:a})=>[a,this.setQueryData(a,e,s)]))}getQueryState(t){var s;const e=this.defaultQueryOptions({queryKey:t});return(s=r(this,$).get(e.queryHash))==null?void 0:s.state}removeQueries(t){const e=r(this,$);T.batch(()=>{e.findAll(t).forEach(s=>{e.remove(s)})})}resetQueries(t,e){const s=r(this,$);return T.batch(()=>(s.findAll(t).forEach(a=>{a.reset()}),this.refetchQueries({type:"active",...t},e)))}cancelQueries(t,e={}){const s={revert:!0,...e},a=T.batch(()=>r(this,$).findAll(t).map(i=>i.cancel(s)));return Promise.all(a).then(L).catch(L)}invalidateQueries(t,e={}){return T.batch(()=>(r(this,$).findAll(t).forEach(s=>{s.invalidate()}),(t==null?void 0:t.refetchType)==="none"?Promise.resolve():this.refetchQueries({...t,type:(t==null?void 0:t.refetchType)??(t==null?void 0:t.type)??"active"},e)))}refetchQueries(t,e={}){const s={...e,cancelRefetch:e.cancelRefetch??!0},a=T.batch(()=>r(this,$).findAll(t).filter(i=>!i.isDisabled()&&!i.isStatic()).map(i=>{let n=i.fetch(void 0,s);return s.throwOnError||(n=n.catch(L)),i.state.fetchStatus==="paused"?Promise.resolve():n}));return Promise.all(a).then(L)}fetchQuery(t){const e=this.defaultQueryOptions(t);e.retry===void 0&&(e.retry=!1);const s=r(this,$).build(this,e);return s.isStaleByTime(je(e.staleTime,s))?s.fetch(e):Promise.resolve(s.state.data)}prefetchQuery(t){return this.fetchQuery(t).then(L).catch(L)}fetchInfiniteQuery(t){return t.behavior=Ss(t.pages),this.fetchQuery(t)}prefetchInfiniteQuery(t){return this.fetchInfiniteQuery(t).then(L).catch(L)}ensureInfiniteQueryData(t){return t.behavior=Ss(t.pages),this.ensureQueryData(t)}resumePausedMutations(){return Nt.isOnline()?r(this,we).resumePausedMutations():Promise.resolve()}getQueryCache(){return r(this,$)}getMutationCache(){return r(this,we)}getDefaultOptions(){return r(this,Me)}setDefaultOptions(t){d(this,Me,t)}setQueryDefaults(t,e){r(this,We).set(Fe(t),{queryKey:t,defaultOptions:e})}getQueryDefaults(t){const e=[...r(this,We).values()],s={};return e.forEach(a=>{rt(t,a.queryKey)&&Object.assign(s,a.defaultOptions)}),s}setMutationDefaults(t,e){r(this,Ye).set(Fe(t),{mutationKey:t,defaultOptions:e})}getMutationDefaults(t){const e=[...r(this,Ye).values()],s={};return e.forEach(a=>{rt(t,a.mutationKey)&&Object.assign(s,a.defaultOptions)}),s}defaultQueryOptions(t){if(t._defaulted)return t;const e={...r(this,Me).queries,...this.getQueryDefaults(t.queryKey),...t,_defaulted:!0};return e.queryHash||(e.queryHash=ns(e.queryKey,e)),e.refetchOnReconnect===void 0&&(e.refetchOnReconnect=e.networkMode!=="always"),e.throwOnError===void 0&&(e.throwOnError=!!e.suspense),!e.networkMode&&e.persister&&(e.networkMode="offlineFirst"),e.queryFn===cs&&(e.enabled=!1),e}defaultMutationOptions(t){return t!=null&&t._defaulted?t:{...r(this,Me).mutations,...(t==null?void 0:t.mutationKey)&&this.getMutationDefaults(t.mutationKey),...t,_defaulted:!0}}clear(){r(this,$).clear(),r(this,we).clear()}},$=new WeakMap,we=new WeakMap,Me=new WeakMap,We=new WeakMap,Ye=new WeakMap,Ce=new WeakMap,Xe=new WeakMap,et=new WeakMap,Js),ia=m.createContext(void 0),mt=t=>{const e=m.useContext(ia);if(!e)throw new Error("No QueryClient set, use QueryClientProvider to set one");return e},eo=({client:t,children:e})=>(m.useEffect(()=>(t.mount(),()=>{t.unmount()}),[t]),c.jsx(ia.Provider,{value:t,children:e})),na=m.createContext(!1),oa=()=>m.useContext(na);na.Provider;function ir(){let t=!1;return{clearReset:()=>{t=!1},reset:()=>{t=!0},isReset:()=>t}}var nr=m.createContext(ir()),ca=()=>m.useContext(nr),la=(t,e,s)=>{const a=s!=null&&s.state.error&&typeof t.throwOnError=="function"?ls(t.throwOnError,[s.state.error,s]):t.throwOnError;(t.suspense||t.experimental_prefetchInRender||a)&&(e.isReset()||(t.retryOnMount=!1))},ha=t=>{m.useEffect(()=>{t.clearReset()},[t])},ua=({result:t,errorResetBoundary:e,throwOnError:s,query:a,suspense:i})=>t.isError&&!e.isReset()&&!t.isFetching&&a&&(i&&t.data===void 0||ls(s,[t.error,a])),da=t=>{if(t.suspense){const s=i=>i==="static"?i:Math.max(i??1e3,1e3),a=t.staleTime;t.staleTime=typeof a=="function"?(...i)=>s(a(...i)):s(a),typeof t.gcTime=="number"&&(t.gcTime=Math.max(t.gcTime,1e3))}},or=(t,e)=>t.isLoading&&t.isFetching&&!e,Xt=(t,e)=>(t==null?void 0:t.suspense)&&e.isPending,es=(t,e,s)=>e.fetchOptimistic(t).catch(()=>{s.clearReset()});function to({queries:t,...e},s){const a=mt(),i=oa(),n=ca(),o=m.useMemo(()=>t.map(w=>{const j=a.defaultQueryOptions(w);return j._optimisticResults=i?"isRestoring":"optimistic",j}),[t,a,i]);o.forEach(w=>{da(w);const j=a.getQueryCache().get(w.queryHash);la(w,n,j)}),ha(n);const[l]=m.useState(()=>new ar(a,o,e)),[u,f,y]=l.getOptimisticResult(o,e.combine),h=!i&&e.subscribed!==!1;m.useSyncExternalStore(m.useCallback(w=>h?l.subscribe(T.batchCalls(w)):L,[l,h]),()=>l.getCurrentResult(),()=>l.getCurrentResult()),m.useEffect(()=>{l.setQueries(o,e)},[o,e,l]);const g=u.some((w,j)=>Xt(o[j],w))?u.flatMap((w,j)=>{const S=o[j];if(S&&Xt(S,w)){const O=new hs(a,S);return es(S,O,n)}return[]}):[];if(g.length>0)throw Promise.all(g);const N=u.find((w,j)=>{const S=o[j];return S&&ua({result:w,errorResetBoundary:n,throwOnError:S.throwOnError,query:a.getQueryCache().get(S.queryHash),suspense:S.suspense})});if(N!=null&&N.error)throw N.error;return f(y())}function cr(t,e,s){var b,g,N,w;const a=oa(),i=ca(),n=mt(),o=n.defaultQueryOptions(t);(g=(b=n.getDefaultOptions().queries)==null?void 0:b._experimental_beforeQuery)==null||g.call(b,o);const l=n.getQueryCache().get(o.queryHash);o._optimisticResults=a?"isRestoring":"optimistic",da(o),la(o,i,l),ha(i);const u=!n.getQueryCache().get(o.queryHash),[f]=m.useState(()=>new e(n,o)),y=f.getOptimisticResult(o),h=!a&&t.subscribed!==!1;if(m.useSyncExternalStore(m.useCallback(j=>{const S=h?f.subscribe(T.batchCalls(j)):L;return f.updateResult(),S},[f,h]),()=>f.getCurrentResult(),()=>f.getCurrentResult()),m.useEffect(()=>{f.setOptions(o)},[o,f]),Xt(o,y))throw es(o,f,i);if(ua({result:y,errorResetBoundary:i,throwOnError:o.throwOnError,query:l,suspense:o.suspense}))throw y.error;if((w=(N=n.getDefaultOptions().queries)==null?void 0:N._experimental_afterQuery)==null||w.call(N,o,y),o.experimental_prefetchInRender&&!nt.isServer()&&or(y,a)){const j=u?es(o,f,i):l==null?void 0:l.promise;j==null||j.catch(L).finally(()=>{f.updateResult()})}return o.notifyOnChangeProps?y:f.trackResult(y)}function lr(t,e){return cr(t,hs)}function ds(t,e){const s=mt(),[a]=m.useState(()=>new tr(s,t));m.useEffect(()=>{a.setOptions(t)},[a,t]);const i=m.useSyncExternalStore(m.useCallback(o=>a.subscribe(T.batchCalls(o)),[a]),()=>a.getCurrentResult(),()=>a.getCurrentResult()),n=m.useCallback((o,l)=>{a.mutate(o,l).catch(L)},[a]);if(i.error&&ls(a.options.throwOnError,[i.error]))throw i.error;return{...i,mutate:n,mutateAsync:i.mutate}}let hr={data:""},ur=t=>{if(typeof window=="object"){let e=(t?t.querySelector("#_goober"):window._goober)||Object.assign(document.createElement("style"),{innerHTML:" ",id:"_goober"});return e.nonce=window.__nonce__,e.parentNode||(t||document.head).appendChild(e),e.firstChild}return t||hr},dr=/(?:([\u0080-\uFFFF\w-%@]+) *:? *([^{;]+?);|([^;}{]*?) *{)|(}\s*)/g,pr=/\/\*[^]*?\*\/|  +/g,Rs=/\n+/g,fe=(t,e)=>{let s="",a="",i="";for(let n in t){let o=t[n];n[0]=="@"?n[1]=="i"?s=n+" "+o+";":a+=n[1]=="f"?fe(o,n):n+"{"+fe(o,n[1]=="k"?"":e)+"}":typeof o=="object"?a+=fe(o,e?e.replace(/([^,])+/g,l=>n.replace(/([^,]*:\S+\([^)]*\))|([^,])+/g,u=>/&/.test(u)?u.replace(/&/g,l):l?l+" "+u:u)):n):o!=null&&(n=/^--/.test(n)?n:n.replace(/[A-Z]/g,"-$&").toLowerCase(),i+=fe.p?fe.p(n,o):n+":"+o+";")}return s+(e&&i?e+"{"+i+"}":i)+a},ae={},pa=t=>{if(typeof t=="object"){let e="";for(let s in t)e+=s+pa(t[s]);return e}return t},fr=(t,e,s,a,i)=>{let n=pa(t),o=ae[n]||(ae[n]=(u=>{let f=0,y=11;for(;f<u.length;)y=101*y+u.charCodeAt(f++)>>>0;return"go"+y})(n));if(!ae[o]){let u=n!==t?t:(f=>{let y,h,b=[{}];for(;y=dr.exec(f.replace(pr,""));)y[4]?b.shift():y[3]?(h=y[3].replace(Rs," ").trim(),b.unshift(b[0][h]=b[0][h]||{})):b[0][y[1]]=y[2].replace(Rs," ").trim();return b[0]})(t);ae[o]=fe(i?{["@keyframes "+o]:u}:u,s?"":"."+o)}let l=s&&ae.g?ae.g:null;return s&&(ae.g=ae[o]),((u,f,y,h)=>{h?f.data=f.data.replace(h,u):f.data.indexOf(u)===-1&&(f.data=y?u+f.data:f.data+u)})(ae[o],e,a,l),o},yr=(t,e,s)=>t.reduce((a,i,n)=>{let o=e[n];if(o&&o.call){let l=o(s),u=l&&l.props&&l.props.className||/^go/.test(l)&&l;o=u?"."+u:l&&typeof l=="object"?l.props?"":fe(l,""):l===!1?"":l}return a+i+(o??"")},"");function Et(t){let e=this||{},s=t.call?t(e.p):t;return fr(s.unshift?s.raw?yr(s,[].slice.call(arguments,1),e.p):s.reduce((a,i)=>Object.assign(a,i&&i.call?i(e.p):i),{}):s,ur(e.target),e.g,e.o,e.k)}let fa,ts,ss;Et.bind({g:1});let ue=Et.bind({k:1});function mr(t,e,s,a){fe.p=e,fa=t,ts=s,ss=a}function Ne(t,e){let s=this||{};return function(){let a=arguments;function i(n,o){let l=Object.assign({},n),u=l.className||i.className;s.p=Object.assign({theme:ts&&ts()},l),s.o=/ *go\d+/.test(u),l.className=Et.apply(s,a)+(u?" "+u:"");let f=t;return t[0]&&(f=l.as||t,delete l.as),ss&&f[0]&&ss(l),fa(f,l)}return i}}var gr=t=>typeof t=="function",St=(t,e)=>gr(t)?t(e):t,xr=(()=>{let t=0;return()=>(++t).toString()})(),ya=(()=>{let t;return()=>{if(t===void 0&&typeof window<"u"){let e=matchMedia("(prefers-reduced-motion: reduce)");t=!e||e.matches}return t}})(),vr=20,ps="default",ma=(t,e)=>{let{toastLimit:s}=t.settings;switch(e.type){case 0:return{...t,toasts:[e.toast,...t.toasts].slice(0,s)};case 1:return{...t,toasts:t.toasts.map(o=>o.id===e.toast.id?{...o,...e.toast}:o)};case 2:let{toast:a}=e;return ma(t,{type:t.toasts.find(o=>o.id===a.id)?1:0,toast:a});case 3:let{toastId:i}=e;return{...t,toasts:t.toasts.map(o=>o.id===i||i===void 0?{...o,dismissed:!0,visible:!1}:o)};case 4:return e.toastId===void 0?{...t,toasts:[]}:{...t,toasts:t.toasts.filter(o=>o.id!==e.toastId)};case 5:return{...t,pausedAt:e.time};case 6:let n=e.time-(t.pausedAt||0);return{...t,pausedAt:void 0,toasts:t.toasts.map(o=>({...o,pauseDuration:o.pauseDuration+n}))}}},jt=[],ga={toasts:[],pausedAt:void 0,settings:{toastLimit:vr}},se={},xa=(t,e=ps)=>{se[e]=ma(se[e]||ga,t),jt.forEach(([s,a])=>{s===e&&a(se[e])})},va=t=>Object.keys(se).forEach(e=>xa(t,e)),br=t=>Object.keys(se).find(e=>se[e].toasts.some(s=>s.id===t)),Rt=(t=ps)=>e=>{xa(e,t)},kr={blank:4e3,error:4e3,success:2e3,loading:1/0,custom:4e3},wr=(t={},e=ps)=>{let[s,a]=m.useState(se[e]||ga),i=m.useRef(se[e]);m.useEffect(()=>(i.current!==se[e]&&a(se[e]),jt.push([e,a]),()=>{let o=jt.findIndex(([l])=>l===e);o>-1&&jt.splice(o,1)}),[e]);let n=s.toasts.map(o=>{var l,u,f;return{...t,...t[o.type],...o,removeDelay:o.removeDelay||((l=t[o.type])==null?void 0:l.removeDelay)||(t==null?void 0:t.removeDelay),duration:o.duration||((u=t[o.type])==null?void 0:u.duration)||(t==null?void 0:t.duration)||kr[o.type],style:{...t.style,...(f=t[o.type])==null?void 0:f.style,...o.style}}});return{...s,toasts:n}},Mr=(t,e="blank",s)=>({createdAt:Date.now(),visible:!0,dismissed:!1,type:e,ariaProps:{role:"status","aria-live":"polite"},message:t,pauseDuration:0,...s,id:(s==null?void 0:s.id)||xr()}),gt=t=>(e,s)=>{let a=Mr(e,t,s);return Rt(a.toasterId||br(a.id))({type:2,toast:a}),a.id},F=(t,e)=>gt("blank")(t,e);F.error=gt("error");F.success=gt("success");F.loading=gt("loading");F.custom=gt("custom");F.dismiss=(t,e)=>{let s={type:3,toastId:t};e?Rt(e)(s):va(s)};F.dismissAll=t=>F.dismiss(void 0,t);F.remove=(t,e)=>{let s={type:4,toastId:t};e?Rt(e)(s):va(s)};F.removeAll=t=>F.remove(void 0,t);F.promise=(t,e,s)=>{let a=F.loading(e.loading,{...s,...s==null?void 0:s.loading});return typeof t=="function"&&(t=t()),t.then(i=>{let n=e.success?St(e.success,i):void 0;return n?F.success(n,{id:a,...s,...s==null?void 0:s.success}):F.dismiss(a),i}).catch(i=>{let n=e.error?St(e.error,i):void 0;n?F.error(n,{id:a,...s,...s==null?void 0:s.error}):F.dismiss(a)}),t};var Cr=1e3,jr=(t,e="default")=>{let{toasts:s,pausedAt:a}=wr(t,e),i=m.useRef(new Map).current,n=m.useCallback((h,b=Cr)=>{if(i.has(h))return;let g=setTimeout(()=>{i.delete(h),o({type:4,toastId:h})},b);i.set(h,g)},[]);m.useEffect(()=>{if(a)return;let h=Date.now(),b=s.map(g=>{if(g.duration===1/0)return;let N=(g.duration||0)+g.pauseDuration-(h-g.createdAt);if(N<0){g.visible&&F.dismiss(g.id);return}return setTimeout(()=>F.dismiss(g.id,e),N)});return()=>{b.forEach(g=>g&&clearTimeout(g))}},[s,a,e]);let o=m.useCallback(Rt(e),[e]),l=m.useCallback(()=>{o({type:5,time:Date.now()})},[o]),u=m.useCallback((h,b)=>{o({type:1,toast:{id:h,height:b}})},[o]),f=m.useCallback(()=>{a&&o({type:6,time:Date.now()})},[a,o]),y=m.useCallback((h,b)=>{let{reverseOrder:g=!1,gutter:N=8,defaultPosition:w}=b||{},j=s.filter(k=>(k.position||w)===(h.position||w)&&k.height),S=j.findIndex(k=>k.id===h.id),O=j.filter((k,M)=>M<S&&k.visible).length;return j.filter(k=>k.visible).slice(...g?[O+1]:[0,O]).reduce((k,M)=>k+(M.height||0)+N,0)},[s]);return m.useEffect(()=>{s.forEach(h=>{if(h.dismissed)n(h.id,h.removeDelay);else{let b=i.get(h.id);b&&(clearTimeout(b),i.delete(h.id))}})},[s,n]),{toasts:s,handlers:{updateHeight:u,startPause:l,endPause:f,calculateOffset:y}}},Nr=ue`
from {
  transform: scale(0) rotate(45deg);
	opacity: 0;
}
to {
 transform: scale(1) rotate(45deg);
  opacity: 1;
}`,Sr=ue`
from {
  transform: scale(0);
  opacity: 0;
}
to {
  transform: scale(1);
  opacity: 1;
}`,_r=ue`
from {
  transform: scale(0) rotate(90deg);
	opacity: 0;
}
to {
  transform: scale(1) rotate(90deg);
	opacity: 1;
}`,Er=Ne("div")`
  width: 20px;
  opacity: 0;
  height: 20px;
  border-radius: 10px;
  background: ${t=>t.primary||"#ff4b4b"};
  position: relative;
  transform: rotate(45deg);

  animation: ${Nr} 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275)
    forwards;
  animation-delay: 100ms;

  &:after,
  &:before {
    content: '';
    animation: ${Sr} 0.15s ease-out forwards;
    animation-delay: 150ms;
    position: absolute;
    border-radius: 3px;
    opacity: 0;
    background: ${t=>t.secondary||"#fff"};
    bottom: 9px;
    left: 4px;
    height: 2px;
    width: 12px;
  }

  &:before {
    animation: ${_r} 0.15s ease-out forwards;
    animation-delay: 180ms;
    transform: rotate(90deg);
  }
`,Rr=ue`
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
`,Or=Ne("div")`
  width: 12px;
  height: 12px;
  box-sizing: border-box;
  border: 2px solid;
  border-radius: 100%;
  border-color: ${t=>t.secondary||"#e0e0e0"};
  border-right-color: ${t=>t.primary||"#616161"};
  animation: ${Rr} 1s linear infinite;
`,Pr=ue`
from {
  transform: scale(0) rotate(45deg);
	opacity: 0;
}
to {
  transform: scale(1) rotate(45deg);
	opacity: 1;
}`,$r=ue`
0% {
	height: 0;
	width: 0;
	opacity: 0;
}
40% {
  height: 0;
	width: 6px;
	opacity: 1;
}
100% {
  opacity: 1;
  height: 10px;
}`,Tr=Ne("div")`
  width: 20px;
  opacity: 0;
  height: 20px;
  border-radius: 10px;
  background: ${t=>t.primary||"#61d345"};
  position: relative;
  transform: rotate(45deg);

  animation: ${Pr} 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275)
    forwards;
  animation-delay: 100ms;
  &:after {
    content: '';
    box-sizing: border-box;
    animation: ${$r} 0.2s ease-out forwards;
    opacity: 0;
    animation-delay: 200ms;
    position: absolute;
    border-right: 2px solid;
    border-bottom: 2px solid;
    border-color: ${t=>t.secondary||"#fff"};
    bottom: 6px;
    left: 6px;
    height: 10px;
    width: 6px;
  }
`,Ar=Ne("div")`
  position: absolute;
`,Ir=Ne("div")`
  position: relative;
  display: flex;
  justify-content: center;
  align-items: center;
  min-width: 20px;
  min-height: 20px;
`,Fr=ue`
from {
  transform: scale(0.6);
  opacity: 0.4;
}
to {
  transform: scale(1);
  opacity: 1;
}`,Dr=Ne("div")`
  position: relative;
  transform: scale(0.6);
  opacity: 0.4;
  min-width: 20px;
  animation: ${Fr} 0.3s 0.12s cubic-bezier(0.175, 0.885, 0.32, 1.275)
    forwards;
`,qr=({toast:t})=>{let{icon:e,type:s,iconTheme:a}=t;return e!==void 0?typeof e=="string"?m.createElement(Dr,null,e):e:s==="blank"?null:m.createElement(Ir,null,m.createElement(Or,{...a}),s!=="loading"&&m.createElement(Ar,null,s==="error"?m.createElement(Er,{...a}):m.createElement(Tr,{...a})))},Qr=t=>`
0% {transform: translate3d(0,${t*-200}%,0) scale(.6); opacity:.5;}
100% {transform: translate3d(0,0,0) scale(1); opacity:1;}
`,Lr=t=>`
0% {transform: translate3d(0,0,-1px) scale(1); opacity:1;}
100% {transform: translate3d(0,${t*-150}%,-1px) scale(.6); opacity:0;}
`,zr="0%{opacity:0;} 100%{opacity:1;}",Ur="0%{opacity:1;} 100%{opacity:0;}",Hr=Ne("div")`
  display: flex;
  align-items: center;
  background: #fff;
  color: #363636;
  line-height: 1.3;
  will-change: transform;
  box-shadow: 0 3px 10px rgba(0, 0, 0, 0.1), 0 3px 3px rgba(0, 0, 0, 0.05);
  max-width: 350px;
  pointer-events: auto;
  padding: 8px 10px;
  border-radius: 8px;
`,Kr=Ne("div")`
  display: flex;
  justify-content: center;
  margin: 4px 10px;
  color: inherit;
  flex: 1 1 auto;
  white-space: pre-line;
`,Vr=(t,e)=>{let s=t.includes("top")?1:-1,[a,i]=ya()?[zr,Ur]:[Qr(s),Lr(s)];return{animation:e?`${ue(a)} 0.35s cubic-bezier(.21,1.02,.73,1) forwards`:`${ue(i)} 0.4s forwards cubic-bezier(.06,.71,.55,1)`}},Br=m.memo(({toast:t,position:e,style:s,children:a})=>{let i=t.height?Vr(t.position||e||"top-center",t.visible):{opacity:0},n=m.createElement(qr,{toast:t}),o=m.createElement(Kr,{...t.ariaProps},St(t.message,t));return m.createElement(Hr,{className:t.className,style:{...i,...s,...t.style}},typeof a=="function"?a({icon:n,message:o}):m.createElement(m.Fragment,null,n,o))});mr(m.createElement);var Gr=({id:t,className:e,style:s,onHeightUpdate:a,children:i})=>{let n=m.useCallback(o=>{if(o){let l=()=>{let u=o.getBoundingClientRect().height;a(t,u)};l(),new MutationObserver(l).observe(o,{subtree:!0,childList:!0,characterData:!0})}},[t,a]);return m.createElement("div",{ref:n,className:e,style:s},i)},Jr=(t,e)=>{let s=t.includes("top"),a=s?{top:0}:{bottom:0},i=t.includes("center")?{justifyContent:"center"}:t.includes("right")?{justifyContent:"flex-end"}:{};return{left:0,right:0,display:"flex",position:"absolute",transition:ya()?void 0:"all 230ms cubic-bezier(.21,1.02,.73,1)",transform:`translateY(${e*(s?1:-1)}px)`,...a,...i}},Zr=Et`
  z-index: 9999;
  > * {
    pointer-events: auto;
  }
`,Mt=16,so=({reverseOrder:t,position:e="top-center",toastOptions:s,gutter:a,children:i,toasterId:n,containerStyle:o,containerClassName:l})=>{let{toasts:u,handlers:f}=jr(s,n);return m.createElement("div",{"data-rht-toaster":n||"",style:{position:"fixed",zIndex:9999,top:Mt,left:Mt,right:Mt,bottom:Mt,pointerEvents:"none",...o},className:l,onMouseEnter:f.startPause,onMouseLeave:f.endPause},u.map(y=>{let h=y.position||e,b=f.calculateOffset(y,{reverseOrder:t,gutter:a,defaultPosition:e}),g=Jr(h,b);return m.createElement(Gr,{id:y.id,key:y.id,onHeightUpdate:f.updateHeight,className:y.visible?Zr:"",style:g},y.type==="custom"?St(y.message,y):i?i(y):m.createElement(Br,{toast:y,position:h}))}))},Y=F;/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Wr=t=>t.replace(/([a-z0-9])([A-Z])/g,"$1-$2").toLowerCase(),Yr=t=>t.replace(/^([A-Z])|[\s-_]+(\w)/g,(e,s,a)=>a?a.toUpperCase():s.toLowerCase()),Os=t=>{const e=Yr(t);return e.charAt(0).toUpperCase()+e.slice(1)},ba=(...t)=>t.filter((e,s,a)=>!!e&&e.trim()!==""&&a.indexOf(e)===s).join(" ").trim(),Xr=t=>{for(const e in t)if(e.startsWith("aria-")||e==="role"||e==="title")return!0};/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */var ei={xmlns:"http://www.w3.org/2000/svg",width:24,height:24,viewBox:"0 0 24 24",fill:"none",stroke:"currentColor",strokeWidth:2,strokeLinecap:"round",strokeLinejoin:"round"};/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ti=m.forwardRef(({color:t="currentColor",size:e=24,strokeWidth:s=2,absoluteStrokeWidth:a,className:i="",children:n,iconNode:o,...l},u)=>m.createElement("svg",{ref:u,...ei,width:e,height:e,stroke:t,strokeWidth:a?Number(s)*24/Number(e):s,className:ba("lucide",i),...!n&&!Xr(l)&&{"aria-hidden":"true"},...l},[...o.map(([f,y])=>m.createElement(f,y)),...Array.isArray(n)?n:[n]]));/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const p=(t,e)=>{const s=m.forwardRef(({className:a,...i},n)=>m.createElement(ti,{ref:n,iconNode:e,className:ba(`lucide-${Wr(Os(t))}`,`lucide-${t}`,a),...i}));return s.displayName=Os(t),s};/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const si=[["path",{d:"M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2",key:"169zse"}]],ao=p("activity",si);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ai=[["rect",{width:"20",height:"5",x:"2",y:"3",rx:"1",key:"1wp1u1"}],["path",{d:"M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8",key:"1s80jp"}],["path",{d:"M10 12h4",key:"a56b0p"}]],ro=p("archive",ai);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ri=[["path",{d:"M5 12h14",key:"1ays0h"}],["path",{d:"m12 5 7 7-7 7",key:"xquz4c"}]],io=p("arrow-right",ri);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ii=[["path",{d:"M10.268 21a2 2 0 0 0 3.464 0",key:"vwvbt9"}],["path",{d:"M3.262 15.326A1 1 0 0 0 4 17h16a1 1 0 0 0 .74-1.673C19.41 13.956 18 12.499 18 8A6 6 0 0 0 6 8c0 4.499-1.411 5.956-2.738 7.326",key:"11g9vi"}]],no=p("bell",ii);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ni=[["path",{d:"M12 7v14",key:"1akyts"}],["path",{d:"M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z",key:"ruj8y"}]],oo=p("book-open",ni);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const oi=[["path",{d:"M12 8V4H8",key:"hb8ula"}],["rect",{width:"16",height:"12",x:"4",y:"8",rx:"2",key:"enze0r"}],["path",{d:"M2 14h2",key:"vft8re"}],["path",{d:"M20 14h2",key:"4cs60a"}],["path",{d:"M15 13v2",key:"1xurst"}],["path",{d:"M9 13v2",key:"rq6x2g"}]],co=p("bot",oi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ci=[["path",{d:"M2.97 12.92A2 2 0 0 0 2 14.63v3.24a2 2 0 0 0 .97 1.71l3 1.8a2 2 0 0 0 2.06 0L12 19v-5.5l-5-3-4.03 2.42Z",key:"lc1i9w"}],["path",{d:"m7 16.5-4.74-2.85",key:"1o9zyk"}],["path",{d:"m7 16.5 5-3",key:"va8pkn"}],["path",{d:"M7 16.5v5.17",key:"jnp8gn"}],["path",{d:"M12 13.5V19l3.97 2.38a2 2 0 0 0 2.06 0l3-1.8a2 2 0 0 0 .97-1.71v-3.24a2 2 0 0 0-.97-1.71L17 10.5l-5 3Z",key:"8zsnat"}],["path",{d:"m17 16.5-5-3",key:"8arw3v"}],["path",{d:"m17 16.5 4.74-2.85",key:"8rfmw"}],["path",{d:"M17 16.5v5.17",key:"k6z78m"}],["path",{d:"M7.97 4.42A2 2 0 0 0 7 6.13v4.37l5 3 5-3V6.13a2 2 0 0 0-.97-1.71l-3-1.8a2 2 0 0 0-2.06 0l-3 1.8Z",key:"1xygjf"}],["path",{d:"M12 8 7.26 5.15",key:"1vbdud"}],["path",{d:"m12 8 4.74-2.85",key:"3rx089"}],["path",{d:"M12 13.5V8",key:"1io7kd"}]],lo=p("boxes",ci);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const li=[["path",{d:"M12 18V5",key:"adv99a"}],["path",{d:"M15 13a4.17 4.17 0 0 1-3-4 4.17 4.17 0 0 1-3 4",key:"1e3is1"}],["path",{d:"M17.598 6.5A3 3 0 1 0 12 5a3 3 0 1 0-5.598 1.5",key:"1gqd8o"}],["path",{d:"M17.997 5.125a4 4 0 0 1 2.526 5.77",key:"iwvgf7"}],["path",{d:"M18 18a4 4 0 0 0 2-7.464",key:"efp6ie"}],["path",{d:"M19.967 17.483A4 4 0 1 1 12 18a4 4 0 1 1-7.967-.517",key:"1gq6am"}],["path",{d:"M6 18a4 4 0 0 1-2-7.464",key:"k1g0md"}],["path",{d:"M6.003 5.125a4 4 0 0 0-2.526 5.77",key:"q97ue3"}]],ho=p("brain",li);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const hi=[["path",{d:"M12 20v-9",key:"1qisl0"}],["path",{d:"M14 7a4 4 0 0 1 4 4v3a6 6 0 0 1-12 0v-3a4 4 0 0 1 4-4z",key:"uouzyp"}],["path",{d:"M14.12 3.88 16 2",key:"qol33r"}],["path",{d:"M21 21a4 4 0 0 0-3.81-4",key:"1b0z45"}],["path",{d:"M21 5a4 4 0 0 1-3.55 3.97",key:"5cxbf6"}],["path",{d:"M22 13h-4",key:"1jl80f"}],["path",{d:"M3 21a4 4 0 0 1 3.81-4",key:"1fjd4g"}],["path",{d:"M3 5a4 4 0 0 0 3.55 3.97",key:"1d7oge"}],["path",{d:"M6 13H2",key:"82j7cp"}],["path",{d:"m8 2 1.88 1.88",key:"fmnt4t"}],["path",{d:"M9 7.13V6a3 3 0 1 1 6 0v1.13",key:"1vgav8"}]],uo=p("bug",hi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ui=[["path",{d:"M16 14v2.2l1.6 1",key:"fo4ql5"}],["path",{d:"M16 2v4",key:"4m81vk"}],["path",{d:"M21 7.5V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h3.5",key:"1osxxc"}],["path",{d:"M3 10h5",key:"r794hk"}],["path",{d:"M8 2v4",key:"1cmpym"}],["circle",{cx:"16",cy:"16",r:"6",key:"qoo3c4"}]],po=p("calendar-clock",ui);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const di=[["path",{d:"M8 2v4",key:"1cmpym"}],["path",{d:"M16 2v4",key:"4m81vk"}],["rect",{width:"18",height:"18",x:"3",y:"4",rx:"2",key:"1hopcy"}],["path",{d:"M3 10h18",key:"8toen8"}]],fo=p("calendar",di);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const pi=[["path",{d:"M3 3v16a2 2 0 0 0 2 2h16",key:"c24i48"}],["path",{d:"M18 17V9",key:"2bz60n"}],["path",{d:"M13 17V5",key:"1frdt8"}],["path",{d:"M8 17v-3",key:"17ska0"}]],yo=p("chart-column",pi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const fi=[["path",{d:"M5 21v-6",key:"1hz6c0"}],["path",{d:"M12 21V3",key:"1lcnhd"}],["path",{d:"M19 21V9",key:"unv183"}]],mo=p("chart-no-axes-column",fi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const yi=[["path",{d:"M20 6 9 17l-5-5",key:"1gmf2c"}]],mi=p("check",yi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const gi=[["path",{d:"m6 9 6 6 6-6",key:"qrunsl"}]],xi=p("chevron-down",gi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const vi=[["path",{d:"m15 18-6-6 6-6",key:"1wnfg3"}]],go=p("chevron-left",vi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const bi=[["path",{d:"m9 18 6-6-6-6",key:"mthhwq"}]],xo=p("chevron-right",bi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ki=[["path",{d:"m18 15-6-6-6 6",key:"153udz"}]],wi=p("chevron-up",ki);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Mi=[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["line",{x1:"12",x2:"12",y1:"8",y2:"12",key:"1pkeuh"}],["line",{x1:"12",x2:"12.01",y1:"16",y2:"16",key:"4dfq90"}]],vo=p("circle-alert",Mi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ci=[["path",{d:"M21.801 10A10 10 0 1 1 17 3.335",key:"yps3ct"}],["path",{d:"m9 11 3 3L22 4",key:"1pflzl"}]],ji=p("circle-check-big",Ci);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ni=[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3",key:"1u773s"}],["path",{d:"M12 17h.01",key:"p32p05"}]],bo=p("circle-question-mark",Ni);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Si=[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"m15 9-6 6",key:"1uzhvr"}],["path",{d:"m9 9 6 6",key:"z0biqf"}]],ko=p("circle-x",Si);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const _i=[["rect",{width:"8",height:"4",x:"8",y:"2",rx:"1",ry:"1",key:"tgr4d6"}],["path",{d:"M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2",key:"116196"}],["path",{d:"M12 11h4",key:"1jrz19"}],["path",{d:"M12 16h4",key:"n85exb"}],["path",{d:"M8 11h.01",key:"1dfujw"}],["path",{d:"M8 16h.01",key:"18s6g9"}]],wo=p("clipboard-list",_i);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ei=[["rect",{width:"8",height:"4",x:"8",y:"2",rx:"1",ry:"1",key:"tgr4d6"}],["path",{d:"M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2",key:"116196"}]],Mo=p("clipboard",Ei);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ri=[["path",{d:"M12 6v6l4 2",key:"mmk7yg"}],["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}]],Oi=p("clock",Ri);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Pi=[["path",{d:"M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z",key:"p7xjir"}]],Co=p("cloud",Pi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const $i=[["path",{d:"m16 18 6-6-6-6",key:"eg8j8"}],["path",{d:"m8 6-6 6 6 6",key:"ppft3o"}]],jo=p("code",$i);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ti=[["rect",{width:"14",height:"14",x:"8",y:"8",rx:"2",ry:"2",key:"17jyea"}],["path",{d:"M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2",key:"zix9uf"}]],Ai=p("copy",Ti);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ii=[["path",{d:"M12 20v2",key:"1lh1kg"}],["path",{d:"M12 2v2",key:"tus03m"}],["path",{d:"M17 20v2",key:"1rnc9c"}],["path",{d:"M17 2v2",key:"11trls"}],["path",{d:"M2 12h2",key:"1t8f8n"}],["path",{d:"M2 17h2",key:"7oei6x"}],["path",{d:"M2 7h2",key:"asdhe0"}],["path",{d:"M20 12h2",key:"1q8mjw"}],["path",{d:"M20 17h2",key:"1fpfkl"}],["path",{d:"M20 7h2",key:"1o8tra"}],["path",{d:"M7 20v2",key:"4gnj0m"}],["path",{d:"M7 2v2",key:"1i4yhu"}],["rect",{x:"4",y:"4",width:"16",height:"16",rx:"2",key:"1vbyd7"}],["rect",{x:"8",y:"8",width:"8",height:"8",rx:"1",key:"z9xiuo"}]],No=p("cpu",Ii);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Fi=[["rect",{width:"20",height:"14",x:"2",y:"5",rx:"2",key:"ynyp8z"}],["line",{x1:"2",x2:"22",y1:"10",y2:"10",key:"1b3vmo"}]],So=p("credit-card",Fi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Di=[["ellipse",{cx:"12",cy:"5",rx:"9",ry:"3",key:"msslwz"}],["path",{d:"M3 5V19A9 3 0 0 0 21 19V5",key:"1wlel7"}],["path",{d:"M3 12A9 3 0 0 0 21 12",key:"mv7ke4"}]],ka=p("database",Di);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const qi=[["path",{d:"M12 15V3",key:"m9g1x1"}],["path",{d:"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4",key:"ih7n3h"}],["path",{d:"m7 10 5 5 5-5",key:"brsn70"}]],Ps=p("download",qi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Qi=[["path",{d:"M15 3h6v6",key:"1q9fwt"}],["path",{d:"M10 14 21 3",key:"gplh6r"}],["path",{d:"M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",key:"a6xqqp"}]],_o=p("external-link",Qi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Li=[["path",{d:"M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49",key:"ct8e1f"}],["path",{d:"M14.084 14.158a3 3 0 0 1-4.242-4.242",key:"151rxh"}],["path",{d:"M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143",key:"13bj9a"}],["path",{d:"m2 2 20 20",key:"1ooewy"}]],Eo=p("eye-off",Li);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const zi=[["path",{d:"M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0",key:"1nclc0"}],["circle",{cx:"12",cy:"12",r:"3",key:"1v7zrd"}]],Ro=p("eye",zi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ui=[["path",{d:"M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z",key:"1oefj6"}],["path",{d:"M14 2v5a1 1 0 0 0 1 1h5",key:"wfsgrz"}],["path",{d:"M10 12.5 8 15l2 2.5",key:"1tg20x"}],["path",{d:"m14 12.5 2 2.5-2 2.5",key:"yinavb"}]],Oo=p("file-code",Ui);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Hi=[["path",{d:"M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z",key:"1oefj6"}],["path",{d:"M14 2v5a1 1 0 0 0 1 1h5",key:"wfsgrz"}],["path",{d:"M10 9H8",key:"b1mrlr"}],["path",{d:"M16 13H8",key:"t4e002"}],["path",{d:"M16 17H8",key:"z1uh3a"}]],Po=p("file-text",Hi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ki=[["path",{d:"M12 3q1 4 4 6.5t3 5.5a1 1 0 0 1-14 0 5 5 0 0 1 1-3 1 1 0 0 0 5 0c0-2-1.5-3-1.5-5q0-2 2.5-4",key:"1slcih"}]],$o=p("flame",Ki);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Vi=[["path",{d:"M14 2v6a2 2 0 0 0 .245.96l5.51 10.08A2 2 0 0 1 18 22H6a2 2 0 0 1-1.755-2.96l5.51-10.08A2 2 0 0 0 10 8V2",key:"18mbvz"}],["path",{d:"M6.453 15h11.094",key:"3shlmq"}],["path",{d:"M8.5 2h7",key:"csnxdl"}]],To=p("flask-conical",Vi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Bi=[["path",{d:"M10 20a1 1 0 0 0 .553.895l2 1A1 1 0 0 0 14 21v-7a2 2 0 0 1 .517-1.341L21.74 4.67A1 1 0 0 0 21 3H3a1 1 0 0 0-.742 1.67l7.225 7.989A2 2 0 0 1 10 14z",key:"sc7q7i"}]],$s=p("funnel",Bi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Gi=[["circle",{cx:"18",cy:"18",r:"3",key:"1xkwt0"}],["circle",{cx:"6",cy:"6",r:"3",key:"1lh9wr"}],["path",{d:"M6 21V9a9 9 0 0 0 9 9",key:"7kw0sc"}]],wa=p("git-merge",Gi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ji=[["path",{d:"M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4",key:"tonef"}],["path",{d:"M9 18c-4.51 2-5-2-7-2",key:"9comsn"}]],Ao=p("github",Ji);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Zi=[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20",key:"13o1zl"}],["path",{d:"M2 12h20",key:"9i4pu4"}]],Io=p("globe",Zi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Wi=[["line",{x1:"22",x2:"2",y1:"12",y2:"12",key:"1y58io"}],["path",{d:"M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z",key:"oot6mr"}],["line",{x1:"6",x2:"6.01",y1:"16",y2:"16",key:"sgf278"}],["line",{x1:"10",x2:"10.01",y1:"16",y2:"16",key:"1l4acy"}]],Fo=p("hard-drive",Wi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Yi=[["polyline",{points:"22 12 16 12 14 15 10 15 8 12 2 12",key:"o97t9d"}],["path",{d:"M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z",key:"oot6mr"}]],Do=p("inbox",Yi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Xi=[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"M12 16v-4",key:"1dtifu"}],["path",{d:"M12 8h.01",key:"e9boi3"}]],qo=p("info",Xi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const en=[["path",{d:"M10 8h.01",key:"1r9ogq"}],["path",{d:"M12 12h.01",key:"1mp3jc"}],["path",{d:"M14 8h.01",key:"1primd"}],["path",{d:"M16 12h.01",key:"1l6xoz"}],["path",{d:"M18 8h.01",key:"emo2bl"}],["path",{d:"M6 8h.01",key:"x9i8wu"}],["path",{d:"M7 16h10",key:"wp8him"}],["path",{d:"M8 12h.01",key:"czm47f"}],["rect",{width:"20",height:"16",x:"2",y:"4",rx:"2",key:"18n3k1"}]],Qo=p("keyboard",en);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const tn=[["rect",{width:"7",height:"9",x:"3",y:"3",rx:"1",key:"10lvy0"}],["rect",{width:"7",height:"5",x:"14",y:"3",rx:"1",key:"16une8"}],["rect",{width:"7",height:"9",x:"14",y:"12",rx:"1",key:"1hutg5"}],["rect",{width:"7",height:"5",x:"3",y:"16",rx:"1",key:"ldoo1y"}]],Lo=p("layout-dashboard",tn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const sn=[["path",{d:"M3 5h.01",key:"18ugdj"}],["path",{d:"M3 12h.01",key:"nlz23k"}],["path",{d:"M3 19h.01",key:"noohij"}],["path",{d:"M8 5h13",key:"1pao27"}],["path",{d:"M8 12h13",key:"1za7za"}],["path",{d:"M8 19h13",key:"m83p4d"}]],zo=p("list",sn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const an=[["path",{d:"M21 12a9 9 0 1 1-6.219-8.56",key:"13zald"}]],Uo=p("loader-circle",an);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const rn=[["rect",{width:"18",height:"11",x:"3",y:"11",rx:"2",ry:"2",key:"1w4ew1"}],["path",{d:"M7 11V7a5 5 0 0 1 10 0v4",key:"fwvmzm"}]],Ho=p("lock",rn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const nn=[["path",{d:"m22 7-8.991 5.727a2 2 0 0 1-2.009 0L2 7",key:"132q7q"}],["rect",{x:"2",y:"4",width:"20",height:"16",rx:"2",key:"izxlao"}]],Ko=p("mail",nn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const on=[["path",{d:"M22 17a2 2 0 0 1-2 2H6.828a2 2 0 0 0-1.414.586l-2.202 2.202A.71.71 0 0 1 2 21.286V5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2z",key:"18887p"}]],Vo=p("message-square",on);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const cn=[["rect",{width:"20",height:"14",x:"2",y:"3",rx:"2",key:"48i651"}],["line",{x1:"8",x2:"16",y1:"21",y2:"21",key:"1svkeh"}],["line",{x1:"12",x2:"12",y1:"17",y2:"21",key:"vw1qmm"}]],Bo=p("monitor",cn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ln=[["path",{d:"M15 18h-5",key:"95g1m2"}],["path",{d:"M18 14h-8",key:"sponae"}],["path",{d:"M4 22h16a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2H8a2 2 0 0 0-2 2v16a2 2 0 0 1-4 0v-9a2 2 0 0 1 2-2h2",key:"39pd36"}],["rect",{width:"8",height:"4",x:"10",y:"6",rx:"1",key:"aywv1n"}]],Go=p("newspaper",ln);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const hn=[["path",{d:"M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z",key:"1a0edw"}],["path",{d:"M12 22V12",key:"d0xqtd"}],["polyline",{points:"3.29 7 12 12 20.71 7",key:"ousv84"}],["path",{d:"m7.5 4.27 9 5.15",key:"1c824w"}]],Jo=p("package",hn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const un=[["rect",{x:"14",y:"3",width:"5",height:"18",rx:"1",key:"kaeet6"}],["rect",{x:"5",y:"3",width:"5",height:"18",rx:"1",key:"1wsw3u"}]],Zo=p("pause",un);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const dn=[["path",{d:"M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z",key:"1a8usu"}],["path",{d:"m15 5 4 4",key:"1mk7zo"}]],Wo=p("pencil",dn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const pn=[["path",{d:"M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z",key:"10ikf1"}]],Yo=p("play",pn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const fn=[["path",{d:"M12 22v-5",key:"1ega77"}],["path",{d:"M15 8V2",key:"18g5xt"}],["path",{d:"M17 8a1 1 0 0 1 1 1v4a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V9a1 1 0 0 1 1-1z",key:"1xoxul"}],["path",{d:"M9 8V2",key:"14iosj"}]],Xo=p("plug",fn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const yn=[["path",{d:"M5 12h14",key:"1ays0h"}],["path",{d:"M12 5v14",key:"s699le"}]],ec=p("plus",yn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const mn=[["path",{d:"M16.247 7.761a6 6 0 0 1 0 8.478",key:"1fwjs5"}],["path",{d:"M19.075 4.933a10 10 0 0 1 0 14.134",key:"ehdyv1"}],["path",{d:"M4.925 19.067a10 10 0 0 1 0-14.134",key:"1q22gi"}],["path",{d:"M7.753 16.239a6 6 0 0 1 0-8.478",key:"r2q7qm"}],["circle",{cx:"12",cy:"12",r:"2",key:"1c9p78"}]],Ma=p("radio",mn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const gn=[["path",{d:"M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8",key:"v9h5vc"}],["path",{d:"M21 3v5h-5",key:"1q7to0"}],["path",{d:"M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16",key:"3uifl3"}],["path",{d:"M8 16H3v5",key:"1cv678"}]],tc=p("refresh-cw",gn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const xn=[["path",{d:"m21 21-4.34-4.34",key:"14j7rj"}],["circle",{cx:"11",cy:"11",r:"8",key:"4ej97u"}]],Ts=p("search",xn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const vn=[["path",{d:"M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z",key:"1ffxy3"}],["path",{d:"m21.854 2.147-10.94 10.939",key:"12cjpa"}]],sc=p("send",vn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const bn=[["rect",{width:"20",height:"8",x:"2",y:"2",rx:"2",ry:"2",key:"ngkwjq"}],["rect",{width:"20",height:"8",x:"2",y:"14",rx:"2",ry:"2",key:"iecqi9"}],["line",{x1:"6",x2:"6.01",y1:"6",y2:"6",key:"16zg32"}],["line",{x1:"6",x2:"6.01",y1:"18",y2:"18",key:"nzw8ys"}]],ac=p("server",bn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const kn=[["path",{d:"M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",key:"oel41y"}]],rc=p("shield",kn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const wn=[["path",{d:"m12.5 17-.5-1-.5 1h1z",key:"3me087"}],["path",{d:"M15 22a1 1 0 0 0 1-1v-1a2 2 0 0 0 1.56-3.25 8 8 0 1 0-11.12 0A2 2 0 0 0 8 20v1a1 1 0 0 0 1 1z",key:"1o5pge"}],["circle",{cx:"15",cy:"12",r:"1",key:"1tmaij"}],["circle",{cx:"9",cy:"12",r:"1",key:"1vctgf"}]],ic=p("skull",wn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Mn=[["path",{d:"M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z",key:"1s2grr"}],["path",{d:"M20 2v4",key:"1rf3ol"}],["path",{d:"M22 4h-4",key:"gwowj6"}],["circle",{cx:"4",cy:"20",r:"2",key:"6kqj1y"}]],nc=p("sparkles",Mn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Cn=[["path",{d:"M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z",key:"r04s7s"}]],oc=p("star",Cn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const jn=[["circle",{cx:"9",cy:"12",r:"3",key:"u3jwor"}],["rect",{width:"20",height:"14",x:"2",y:"5",rx:"7",key:"g7kal2"}]],cc=p("toggle-left",jn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Nn=[["circle",{cx:"15",cy:"12",r:"3",key:"1afu0r"}],["rect",{width:"20",height:"14",x:"2",y:"5",rx:"7",key:"g7kal2"}]],lc=p("toggle-right",Nn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Sn=[["path",{d:"M10 11v6",key:"nco0om"}],["path",{d:"M14 11v6",key:"outv1u"}],["path",{d:"M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6",key:"miytrc"}],["path",{d:"M3 6h18",key:"d0wm0j"}],["path",{d:"M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",key:"e791ji"}]],hc=p("trash-2",Sn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const _n=[["path",{d:"M16 7h6v6",key:"box55l"}],["path",{d:"m22 7-8.5 8.5-5-5L2 17",key:"1t1m79"}]],uc=p("trending-up",_n);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const En=[["path",{d:"m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3",key:"wmoenq"}],["path",{d:"M12 9v4",key:"juzpu7"}],["path",{d:"M12 17h.01",key:"p32p05"}]],Ca=p("triangle-alert",En);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Rn=[["path",{d:"M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2",key:"975kel"}],["circle",{cx:"12",cy:"7",r:"4",key:"17ys0d"}]],dc=p("user",Rn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const On=[["path",{d:"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2",key:"1yyitq"}],["path",{d:"M16 3.128a4 4 0 0 1 0 7.744",key:"16gr8j"}],["path",{d:"M22 21v-2a4 4 0 0 0-3-3.87",key:"kshegd"}],["circle",{cx:"9",cy:"7",r:"4",key:"nufk8"}]],pc=p("users",On);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Pn=[["path",{d:"m21.64 3.64-1.28-1.28a1.21 1.21 0 0 0-1.72 0L2.36 18.64a1.21 1.21 0 0 0 0 1.72l1.28 1.28a1.2 1.2 0 0 0 1.72 0L21.64 5.36a1.2 1.2 0 0 0 0-1.72",key:"ul74o6"}],["path",{d:"m14 7 3 3",key:"1r5n42"}],["path",{d:"M5 6v4",key:"ilb8ba"}],["path",{d:"M19 14v4",key:"blhpug"}],["path",{d:"M10 2v2",key:"7u0qdc"}],["path",{d:"M7 8H3",key:"zfb6yr"}],["path",{d:"M21 16h-4",key:"1cnmox"}],["path",{d:"M11 3H9",key:"1obp7u"}]],fc=p("wand-sparkles",Pn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const $n=[["path",{d:"M18 6 6 18",key:"1bl5f8"}],["path",{d:"m6 6 12 12",key:"d8bk6v"}]],yc=p("x",$n);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Tn=[["path",{d:"M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",key:"1xq2db"}]],mc=p("zap",Tn),An="/api/v1";function _t(){const t=document.querySelector('meta[name="spa-token"]');return(t==null?void 0:t.getAttribute("content"))??null}const le=Ra.create({baseURL:An,headers:{"Content-Type":"application/json"},timeout:3e4});le.interceptors.request.use(t=>{const e=_t();return e&&(t.headers["X-SPA-Token"]=e),t});async function ja(t=3){for(let e=0;e<t;e++)try{const s=await fetch("/internal/spa-token",{cache:"no-store"});if(!s.ok){if(e<t-1){await new Promise(n=>setTimeout(n,200*Math.pow(2,e)));continue}return!1}const a=await s.text();if(!(a!=null&&a.trim())){if(e<t-1){await new Promise(n=>setTimeout(n,200*Math.pow(2,e)));continue}return!1}let i=document.querySelector('meta[name="spa-token"]');return i||(i=document.createElement("meta"),i.setAttribute("name","spa-token"),document.head.appendChild(i)),i.setAttribute("content",a.trim()),Na(),!0}catch{if(e<t-1){await new Promise(s=>setTimeout(s,200*Math.pow(2,e)));continue}return!1}return!1}let $t=null;const In=5400*1e3;function Na(){$t&&clearTimeout($t),$t=setTimeout(async()=>{await ja()},In)}_t()&&Na();const st=new Map,As=2e3,Fn=50;function qe(t){const e=Date.now();if(st.size>Fn)for(const[a,i]of st)e-i>As*5&&st.delete(a);const s=st.get(t);return!s||e-s>As?(st.set(t,e),!0):!1}function Dn(t){return["/insights","/$deadletterqueue","/%24deadletterqueue"].some(s=>t.includes(s))}le.interceptors.response.use(t=>t,async t=>{var a,i;if((a=t.config)!=null&&a._silent)return Promise.reject(t);if(!t.response)return qe("network-error")&&Y.error("Cannot reach the API. If running on a remote server, ensure port 5153 is accessible.",{duration:5e3}),Promise.reject(t);const e=t.response.status,s=((i=t.config)==null?void 0:i.url)||"unknown";switch(e){case 401:{const n=t.config,o=(n==null?void 0:n._spaRetryCount)??0;if(o<2&&_t()!==null&&n&&(n._spaRetryCount=o+1,await ja()))return n.headers=n.headers??{},n.headers["X-SPA-Token"]=_t(),le(n);const l=`${e}-${s}`;qe(l)&&Y.error("Session expired. Please refresh the page to continue.",{duration:5e3});break}case 403:{const n=`${e}-${s}`;qe(n)&&Y.error("Access denied. Verify your connection string has the required permissions.",{duration:5e3});break}case 404:{if(!Dn(s)){const n=s.match(/\/messages\/[a-f0-9-]+/i),o=`404-${s}`;qe(o)&&Y.error(n?"Message not found — it may have been consumed, expired, or already replayed.":"Resource not found.",{duration:4e3})}break}case 422:{const n=t.response.data.errors;if(n){const o=`${e}-validation`;qe(o)&&Object.values(n).flat().forEach(l=>Y.error(l,{duration:5e3}))}break}case 500:case 502:case 503:{const n=`${e}-server`;qe(n)&&Y.error("Server error. Try refreshing or restart the API server.",{duration:5e3});break}}return Promise.reject(t)});const fs={list:async()=>(await le.get("/namespaces")).data,create:async t=>(await le.post("/namespaces",t)).data,get:async t=>(await le.get(`/namespaces/${t}`)).data,delete:async t=>{await le.delete(`/namespaces/${t}`)},testConnection:async t=>(await le.post(`/namespaces/${t}/test-connection`)).data};function qn(){return lr({queryKey:["namespaces"],queryFn:fs.list})}function gc(){const t=mt();return ds({mutationFn:e=>fs.create(e),onSuccess:()=>{t.invalidateQueries({queryKey:["namespaces"]}),Y.success("Namespace connected successfully")},onError:e=>{var a,i,n,o;const s=((i=(a=e==null?void 0:e.response)==null?void 0:a.data)==null?void 0:i.detail)||((o=(n=e==null?void 0:e.response)==null?void 0:n.data)==null?void 0:o.message)||(e==null?void 0:e.message)||"Failed to connect namespace. Verify the connection string format and permissions.";Y.error(s,{duration:6e3})}})}function xc(){const t=mt();return ds({mutationFn:e=>fs.delete(e),onSuccess:()=>{t.invalidateQueries({queryKey:["namespaces"]}),Y.success("Namespace deleted")},onError:()=>{Y.error("Failed to delete namespace. The namespace may still be in use.",{duration:5e3})}})}const Qn={searchTimeline:async(t,e)=>{const s={correlationId:t};return e&&(s.namespaceId=e),(await le.get("/correlation/timeline",{params:s})).data}};function Ln(){return ds({mutationFn:({correlationId:t,namespaceId:e})=>Qn.searchTimeline(t,e),onError:t=>{var s,a;const e=((a=(s=t==null?void 0:t.response)==null?void 0:s.data)==null?void 0:a.detail)||(t==null?void 0:t.message)||"Correlation search failed";Y.error(e,{duration:5e3})}})}async function zn(t){try{if(navigator.clipboard&&window.isSecureContext)return await navigator.clipboard.writeText(t),!0;const e=document.createElement("textarea");e.value=t,e.style.position="fixed",e.style.opacity="0",e.style.pointerEvents="none",document.body.appendChild(e),e.focus(),e.select();const s=document.execCommand("copy");return document.body.removeChild(e),s}catch{return!1}}function Sa({text:t,label:e,className:s="",iconSize:a="w-3.5 h-3.5"}){const[i,n]=m.useState(!1),o=m.useCallback(async l=>{l.stopPropagation(),await zn(t)&&(n(!0),setTimeout(()=>n(!1),2e3))},[t]);return c.jsxs("button",{type:"button",onClick:o,title:e?`Copy ${e}`:"Copy to clipboard","aria-label":e?`Copy ${e}`:"Copy to clipboard",className:`inline-flex items-center gap-1 p-1 rounded transition-colors ${i?"text-green-600 bg-green-50":"text-gray-400 hover:text-gray-600 hover:bg-gray-100"} ${s}`,children:[i?c.jsx(mi,{className:a}):c.jsx(Ai,{className:a}),e&&c.jsx("span",{className:"text-xs",children:i?"Copied!":e})]})}function _a(t){switch(t){case"Active":return{bg:"bg-emerald-100",text:"text-emerald-700",dot:"bg-emerald-500"};case"Scheduled":return{bg:"bg-sky-100",text:"text-sky-700",dot:"bg-sky-500"};case"DeadLettered":return{bg:"bg-red-100",text:"text-red-700",dot:"bg-red-500"};case"Replayed":return{bg:"bg-amber-100",text:"text-amber-700",dot:"bg-amber-500"};case"Resolved":return{bg:"bg-gray-100",text:"text-gray-600",dot:"bg-gray-400"};case"Deferred":return{bg:"bg-purple-100",text:"text-purple-700",dot:"bg-purple-500"};default:return{bg:"bg-gray-100",text:"text-gray-600",dot:"bg-gray-400"}}}function as(t){try{return new Date(t).toLocaleString("en-US",{month:"short",day:"numeric",year:"numeric",hour:"2-digit",minute:"2-digit",second:"2-digit"})}catch{return t}}function Un(t){var e;return(e=t.entityPath)!=null&&e.includes("/subscriptions/")?"Topic/Sub":"Queue"}const Hn=[{label:"Last 1 hour",value:"1h"},{label:"Last 6 hours",value:"6h"},{label:"Last 24 hours",value:"24h"},{label:"Last 7 days",value:"7d"},{label:"All time",value:"all"}];function Kn(t,e){if(e==="all")return t;const s=Date.now(),a={"1h":3600*1e3,"6h":360*60*1e3,"24h":1440*60*1e3,"7d":10080*60*1e3,all:1/0},i=s-a[e];return t.filter(n=>new Date(n.timestamp).getTime()>=i)}function Is(t){const e=new Blob([JSON.stringify(t,null,2)],{type:"application/json"}),s=URL.createObjectURL(e),a=document.createElement("a");a.href=s,a.download=`correlation-${t.correlationId}-${new Date().toISOString().slice(0,19).replace(/:/g,"-")}.json`,a.click(),URL.revokeObjectURL(s)}function Ea(t){if(t<1e3)return`${t}ms`;const e=Math.floor(t/1e3);if(e<60)return`${e}s`;const s=Math.floor(e/60);if(s<60)return`${s}m ${e%60}s`;const a=Math.floor(s/60);return a<24?`${a}h ${s%60}m`:`${Math.floor(a/24)}d ${a%24}h`}function Vn({fromTs:t,toTs:e}){const s=new Date(e).getTime()-new Date(t).getTime();return s<=0?null:c.jsxs("div",{className:"flex items-center gap-2 ml-8 my-1 text-xs text-gray-400 select-none",children:[c.jsx("div",{className:"h-px flex-1 border-l-0 border-t border-dashed border-gray-300"}),c.jsxs("span",{className:"shrink-0 px-2 py-0.5 rounded-full bg-gray-100 text-gray-500 font-mono",children:["+",Ea(s)]}),c.jsx("div",{className:"h-px flex-1 border-r-0 border-t border-dashed border-gray-300"})]})}function Bn({entries:t}){if(t.length<2)return null;const e=t.map(f=>new Date(f.timestamp).getTime()),s=Math.min(...e),i=Math.max(...e)-s||1,n=100,o=28,l=14,u=3.5;return c.jsxs("div",{className:"mb-5 bg-white border border-gray-200 rounded-xl px-5 py-3 shadow-sm",children:[c.jsxs("div",{className:"flex items-center justify-between mb-1.5",children:[c.jsxs("span",{className:"text-xs font-semibold text-gray-500 uppercase tracking-wide",children:["Timeline Minimap · ",t.length," event",t.length!==1?"s":""]}),c.jsxs("span",{className:"text-xs text-gray-400 font-mono",children:[Ea(i)," total span"]})]}),c.jsxs("svg",{viewBox:`0 0 ${n} ${o}`,preserveAspectRatio:"none",className:"w-full",style:{height:o},children:[c.jsx("line",{x1:"2",y1:l,x2:"98",y2:l,stroke:"#e5e7eb",strokeWidth:"1.5"}),t.map((f,y)=>{const b=2+(new Date(f.timestamp).getTime()-s)/i*96,{dot:g}=_a(f.state),w={"bg-emerald-500":"#10b981","bg-sky-500":"#0ea5e9","bg-red-500":"#ef4444","bg-amber-500":"#f59e0b","bg-gray-400":"#9ca3af","bg-purple-500":"#a855f7"}[g]??"#9ca3af";return c.jsx("g",{children:c.jsx("circle",{cx:b,cy:l,r:u,fill:w,opacity:"0.9"})},y)})]}),c.jsxs("div",{className:"flex justify-between text-[10px] text-gray-400 font-mono mt-0.5",children:[c.jsx("span",{children:as(t[0].timestamp).split(",")[0]}),c.jsx("span",{children:as(t[t.length-1].timestamp).split(",")[0]})]})]})}function Gn({entry:t,isLast:e,index:s}){const a=_a(t.state),[i,n]=m.useState(!1),o=Un(t);return c.jsxs("div",{className:"flex gap-4",children:[c.jsxs("div",{className:"flex flex-col items-center shrink-0 w-8",children:[c.jsx("span",{className:"text-xs font-bold text-gray-400 text-center leading-none mb-1 pt-3",children:s+1}),c.jsx("div",{className:`w-3 h-3 rounded-full shrink-0 ${a.dot}`}),!e&&c.jsx("div",{className:"w-0.5 flex-1 bg-gray-200 mt-1"})]}),c.jsx("div",{className:"flex-1 min-w-0 mb-4",children:c.jsxs("div",{className:"bg-white border border-gray-200 rounded-xl shadow-sm p-4 overflow-hidden",children:[c.jsxs("div",{className:"flex items-center justify-between mb-2 gap-2 flex-wrap",children:[c.jsxs("div",{className:"flex items-center gap-2 min-w-0 flex-wrap",children:[c.jsx("span",{className:`text-xs font-semibold px-2 py-0.5 rounded-full shrink-0 ${a.bg} ${a.text}`,children:t.state}),c.jsx("span",{className:"text-sm font-medium text-gray-900 truncate",children:t.entityName}),c.jsx("span",{className:`text-xs px-2 py-0.5 rounded-full shrink-0 font-medium ${o==="Queue"?"bg-sky-50 text-sky-600 border border-sky-100":"bg-indigo-50 text-indigo-600 border border-indigo-100"}`,children:o})]}),c.jsx("span",{className:`text-xs px-2 py-0.5 rounded-full shrink-0 font-medium ${t.source==="Live"?"bg-sky-100 text-sky-700":"bg-gray-100 text-gray-600"}`,children:t.source==="Live"?c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsx(Ma,{className:"w-3 h-3"}),"Live"]}):c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsx(ka,{className:"w-3 h-3"}),"History"]})})]}),c.jsxs("div",{className:"flex items-center gap-4 text-xs text-gray-500 mb-2 flex-wrap",children:[c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsx(Oi,{className:"w-3 h-3"}),as(t.timestamp)]}),c.jsxs("span",{children:["SeqNo: ",t.sequenceNumber.toLocaleString()]}),c.jsxs("span",{children:["Size: ",t.sizeInBytes>0?`${(t.sizeInBytes/1024).toFixed(1)} KB`:"—"]}),"            ",t.messageId&&c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsxs("span",{className:"font-mono text-gray-500",children:["ID: ",t.messageId.slice(0,8),"…"]}),c.jsx(Sa,{text:t.messageId,label:"message ID",iconSize:"w-3 h-3"})]}),"          "]}),c.jsxs("p",{className:"text-xs text-gray-500 mb-2",children:["Namespace:"," ",c.jsx("span",{className:"font-medium text-gray-700",children:t.namespaceDisplayName}),t.entityPath&&t.entityPath!==t.entityName&&c.jsxs("span",{className:"ml-1 text-gray-400",children:["(",t.entityPath,")"]})]}),t.bodyPreview&&c.jsxs("div",{className:"mt-1 min-w-0 max-w-full overflow-hidden",children:[c.jsx("div",{className:`text-xs text-gray-600 font-mono bg-gray-50 border border-gray-100 rounded px-2 py-1.5 overflow-hidden ${i?"whitespace-pre-wrap break-all":"truncate"}`,children:i?t.bodyPreview:t.bodyPreview.slice(0,200)+(t.bodyPreview.length>200?"…":"")}),t.bodyPreview.length>200&&c.jsx("button",{onClick:()=>n(l=>!l),className:"mt-1 text-xs text-violet-600 hover:text-violet-800 flex items-center gap-1",children:i?c.jsxs(c.Fragment,{children:[c.jsx(wi,{className:"w-3 h-3"})," Show less"]}):c.jsxs(c.Fragment,{children:[c.jsx(xi,{className:"w-3 h-3"})," Show full body"]})})]}),t.deadLetterReason&&c.jsxs("p",{className:"text-xs text-red-600 mt-2 flex items-center gap-1",children:[c.jsx(Ca,{className:"w-3 h-3 shrink-0"}),"DLQ Reason: ",t.deadLetterReason]})]})})]})}function Jn(){return c.jsxs("div",{className:"flex flex-col items-center justify-center h-full text-center px-8 py-16",children:[c.jsx(wa,{className:"w-14 h-14 text-gray-300 mb-4"}),c.jsx("p",{className:"text-gray-600 font-semibold text-lg mb-1",children:"Enter a Correlation ID"}),c.jsx("p",{className:"text-gray-400 text-sm max-w-sm",children:"Trace a message journey across all your queues and namespaces by entering a Correlation ID above."}),c.jsx("div",{className:"mt-6 grid grid-cols-3 gap-3 text-left w-full max-w-sm",children:[{icon:"🔍",title:"Cross-namespace",body:"Searches all connected namespaces in parallel"},{icon:"📜",title:"Live + History",body:"Merges live queue data with DLQ history"},{icon:"📦",title:"Full journey",body:"Shows message state at every hop"}].map(t=>c.jsxs("div",{className:"bg-white border border-gray-100 rounded-xl p-3",children:[c.jsx("div",{className:"text-lg mb-1",children:t.icon}),c.jsx("p",{className:"text-xs font-semibold text-gray-700 mb-0.5",children:t.title}),c.jsx("p",{className:"text-xs text-gray-400",children:t.body})]},t.title))})]})}function Fs(){const[t,e]=Oa(),s=t.get("correlationId")??"",a=t.get("namespaceId")??"",[i,n]=m.useState(s),[o,l]=m.useState(a),[u,f]=m.useState("all"),[y,h]=m.useState("all"),[b,g]=m.useState(!1),{data:N}=qn(),w=Ln(),j=m.useRef(!1);m.useEffect(()=>{s&&!j.current&&(j.current=!0,w.mutate({correlationId:s,namespaceId:a||void 0}))},[]);function S(){if(!i.trim())return;const v={correlationId:i.trim()};o&&(v.namespaceId=o),e(v),w.mutate({correlationId:i.trim(),namespaceId:o||void 0})}function O(v){v.key==="Enter"&&S()}const k=w.data,M=w.isPending,I=w.isSuccess||w.isError,P=m.useMemo(()=>{if(!(k!=null&&k.entries))return[];let v=Kn(k.entries,u);return y==="queue"?v=v.filter(R=>{var A;return!((A=R.entityPath)!=null&&A.includes("/subscriptions/"))}):y==="topic"&&(v=v.filter(R=>{var A;return(A=R.entityPath)==null?void 0:A.includes("/subscriptions/")})),v},[k==null?void 0:k.entries,u,y]),K=u!=="all"||y!=="all";return c.jsxs("div",{className:"flex-1 flex flex-col overflow-hidden",children:[c.jsx("div",{className:"bg-gradient-to-r from-violet-600 to-violet-500 px-6 py-4 shrink-0",children:c.jsxs("div",{className:"flex items-center justify-between",children:[c.jsxs("div",{className:"flex items-center gap-3",children:[c.jsx(wa,{className:"w-6 h-6 text-white/80"}),c.jsxs("div",{children:[c.jsx("h1",{className:"text-xl font-semibold text-white",children:"Correlation Explorer"}),c.jsx("p",{className:"text-violet-100 text-sm",children:"Trace any message's full journey across all queues and namespaces"})]})]}),k&&k.totalCount>0&&c.jsxs("button",{onClick:()=>Is(k),className:"flex items-center gap-2 px-3 py-1.5 bg-white/20 hover:bg-white/30 text-white rounded-lg text-sm font-medium transition-colors",title:"Export timeline as JSON",children:[c.jsx(Ps,{className:"w-4 h-4"}),"Export JSON"]})]})}),c.jsxs("div",{className:"bg-white border-b border-gray-200 px-3 sm:px-4 lg:px-6 py-3 shrink-0 overflow-x-auto",children:[c.jsxs("div",{className:"flex items-center gap-2 sm:gap-3 flex-wrap",children:[c.jsxs("div",{className:"flex-1 flex items-center gap-2 bg-gray-50 border border-gray-300 rounded-lg px-3 py-2 focus-within:border-violet-400 focus-within:ring-1 focus-within:ring-violet-400 transition-all",children:[c.jsx(Ts,{className:"w-4 h-4 text-gray-400 shrink-0"}),c.jsx("input",{type:"text",value:i,onChange:v=>n(v.target.value),onKeyDown:O,placeholder:"Enter Correlation ID…",className:"flex-1 bg-transparent text-sm text-gray-900 placeholder-gray-400 outline-none","aria-label":"Correlation ID"})]}),c.jsxs("select",{value:o,onChange:v=>l(v.target.value),className:"text-sm border border-gray-300 rounded-lg px-3 py-2 bg-white text-gray-700 focus:border-violet-400 focus:outline-none focus:ring-1 focus:ring-violet-400","aria-label":"Namespace filter",children:[c.jsx("option",{value:"",children:"All Namespaces"}),N==null?void 0:N.map(v=>c.jsx("option",{value:v.id,children:v.displayName??v.name},v.id))]}),c.jsxs("button",{onClick:()=>g(v=>!v),className:`flex items-center gap-1.5 px-2 sm:px-3 py-2 border rounded-lg text-xs sm:text-sm font-medium transition-colors ${K?"border-violet-400 bg-violet-50 text-violet-700":"border-gray-300 bg-white text-gray-700 hover:bg-gray-50"}`,"aria-label":"Toggle result filters",children:[c.jsx($s,{className:"w-4 h-4"}),c.jsx("span",{className:"hidden sm:inline",children:"Filters"}),K&&c.jsx("span",{className:"w-2 h-2 rounded-full bg-violet-500 ml-0.5"})]}),c.jsxs("button",{onClick:S,disabled:!i.trim()||M,className:"flex items-center gap-1.5 px-3 sm:px-4 py-2 bg-violet-600 hover:bg-violet-700 disabled:bg-violet-300 text-white rounded-lg text-xs sm:text-sm font-medium transition-colors whitespace-nowrap",children:[c.jsx(Ts,{className:"w-4 h-4"}),c.jsx("span",{className:"hidden sm:inline",children:M?"Searching…":"Search"}),c.jsx("span",{className:"sm:hidden",children:M?"...":"→"})]})]}),b&&c.jsxs("div",{className:"flex items-center gap-2 sm:gap-4 mt-3 pt-3 border-t border-gray-100 flex-wrap",children:[c.jsxs("div",{className:"flex items-center gap-2",children:[c.jsx("label",{className:"text-xs font-medium text-gray-600",children:"Time range"}),c.jsx("select",{value:u,onChange:v=>f(v.target.value),className:"text-sm border border-gray-200 rounded-lg px-2.5 py-1 bg-white focus:border-violet-400 focus:outline-none focus:ring-1 focus:ring-violet-400",children:Hn.map(v=>c.jsx("option",{value:v.value,children:v.label},v.value))})]}),c.jsxs("div",{className:"flex items-center gap-2",children:[c.jsx("label",{className:"text-xs font-medium text-gray-600",children:"Entity type"}),c.jsx("div",{className:"flex rounded-lg border border-gray-200 overflow-hidden",children:["all","queue","topic"].map(v=>c.jsx("button",{onClick:()=>h(v),className:`px-3 py-1 text-xs font-medium transition-colors ${y===v?"bg-violet-600 text-white":"bg-white text-gray-600 hover:bg-gray-50"}`,children:v==="all"?"All":v==="queue"?"Queues":"Topics"},v))})]}),K&&c.jsx("button",{onClick:()=>{f("all"),h("all")},className:"text-xs text-gray-400 hover:text-gray-600 underline",children:"Clear filters"})]})]}),c.jsx("div",{className:"flex-1 overflow-y-auto overflow-x-hidden bg-gray-50",children:M?c.jsxs("div",{className:"flex flex-col items-center justify-center h-full gap-3 text-gray-500",children:[c.jsx("div",{className:"animate-spin rounded-full border-4 border-violet-200 border-t-violet-600 w-10 h-10"}),c.jsxs("p",{className:"text-sm",children:["Searching across ",(N==null?void 0:N.length)??"…"," namespace(s)…"]})]}):I?k&&k.totalCount===0?c.jsxs("div",{className:"flex flex-col items-center justify-center h-full text-center px-8 py-16",children:[c.jsx(ji,{className:"w-12 h-12 text-gray-300 mb-4"}),c.jsx("p",{className:"text-gray-600 font-semibold text-lg mb-1",children:"No messages found"}),c.jsxs("p",{className:"text-gray-400 text-sm",children:["No messages for correlation ID:"," ",c.jsx("span",{className:"font-mono text-gray-600",children:k.correlationId})]})]}):k?c.jsxs("div",{className:"px-3 sm:px-4 lg:px-6 py-4 sm:py-5 w-full max-w-5xl mx-auto",children:[k.isPartialResult&&c.jsxs("div",{className:"flex items-center gap-2 bg-amber-50 border border-amber-200 rounded-lg px-4 py-2.5 mb-4 text-amber-800 text-sm",children:[c.jsx(Ca,{className:"w-4 h-4 shrink-0 text-amber-600"}),c.jsxs("span",{children:["Search timed out — showing partial results (",k.totalCount," entries found)"]})]}),c.jsxs("div",{className:"flex items-center justify-between mb-4 flex-wrap gap-2",children:[c.jsxs("div",{children:[c.jsxs("p",{className:"text-gray-700 text-sm",children:["Found"," ",c.jsx("span",{className:"font-semibold text-gray-900",children:k.totalCount})," ","message(s) across"," ",c.jsx("span",{className:"font-semibold",children:k.entitiesSearched})," entity/ies in"," ",c.jsx("span",{className:"font-semibold",children:k.namespacesSearched})," namespace(s)"]}),c.jsxs("p",{className:"text-xs text-gray-400 mt-0.5 flex items-center gap-1.5",children:["Search completed in ",k.searchDurationMs.toLocaleString(),"ms",K&&P.length!==k.entries.length&&c.jsxs("span",{className:"ml-2 text-violet-600 font-medium",children:["· Showing ",P.length," of ",k.totalCount," after filters"]}),c.jsxs("span",{className:"flex items-center gap-1 ml-2 font-mono text-gray-500",children:["CorrelationID: ",k.correlationId.slice(0,12),"…",c.jsx(Sa,{text:k.correlationId,label:"correlation ID",iconSize:"w-3 h-3"})]})]})]}),c.jsxs("button",{onClick:()=>Is(k),className:"flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-violet-700 bg-violet-50 hover:bg-violet-100 border border-violet-200 rounded-lg transition-colors",children:[c.jsx(Ps,{className:"w-3.5 h-3.5"}),"Export JSON"]})]}),c.jsxs("div",{className:"flex items-center gap-4 mb-4 text-xs text-gray-500",children:[c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsx(Ma,{className:"w-3 h-3 text-sky-500"}),"Live = currently in queue"]}),c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsx(ka,{className:"w-3 h-3 text-gray-400"}),"History = from DLQ history database"]})]}),P.length===0?c.jsxs("div",{className:"text-center py-12 text-gray-400",children:[c.jsx($s,{className:"w-10 h-10 mx-auto mb-3 opacity-40"}),c.jsx("p",{className:"font-medium",children:"No entries match the current filters"}),c.jsx("button",{onClick:()=>{f("all"),h("all")},className:"mt-2 text-sm text-violet-600 underline",children:"Clear filters"})]}):c.jsxs(c.Fragment,{children:[c.jsx(Bn,{entries:P}),c.jsx("div",{className:"ml-1 sm:ml-2 -mr-4",children:P.map((v,R)=>c.jsxs("div",{children:[R>0&&c.jsx(Vn,{fromTs:P[R-1].timestamp,toTs:v.timestamp}),c.jsx(Gn,{entry:v,isLast:R===P.length-1,index:R})]},`${v.messageId}-${R}`))})]})]}):null:c.jsx(Jn,{})})]})}const vc=Object.freeze(Object.defineProperty({__proto__:null,CorrelationExplorerPage:Fs,default:Fs},Symbol.toStringTag,{value:"Module"}));export{Jo as $,ao as A,ka as B,Oi as C,Ps as D,Ro as E,$o as F,Io as G,hc as H,Do as I,sc as J,qo as K,Ho as L,Ko as M,Go as N,uo as O,ec as P,pc as Q,tc as R,rc as S,uc as T,dc as U,lo as V,fc as W,yc as X,no as Y,mc as Z,So as _,le as a,mi as a0,Uo as a1,ic as a2,Vo as a3,Xo as a4,Qo as a5,co as a6,wo as a7,zn as a8,Ai as a9,Xn as aA,eo as aB,so as aC,vc as aD,nc as aa,Sa as ab,jo as ac,zo as ad,Yo as ae,Mo as af,Zo as ag,gc as ah,xc as ai,Eo as aj,oc as ak,Ao as al,ho as am,lc as an,cc as ao,To as ap,Wo as aq,Fo as ar,No as as,ac as at,oo as au,fo as av,po as aw,Bo as ax,Oo as ay,_o as az,lr as b,qn as c,wa as d,mo as e,Ca as f,ji as g,go as h,xo as i,c as j,bo as k,mt as l,ds as m,Po as n,io as o,ko as p,vo as q,ro as r,yo as s,$s as t,to as u,Co as v,Ts as w,xi as x,Lo as y,Y as z};
