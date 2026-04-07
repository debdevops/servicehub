var ms=t=>{throw TypeError(t)};var Ot=(t,e,s)=>e.has(t)||ms("Cannot "+s);var a=(t,e,s)=>(Ot(t,e,"read from private field"),s?s.call(t):e.get(t)),x=(t,e,s)=>e.has(t)?ms("Cannot add the same private member more than once"):e instanceof WeakSet?e.add(t):e.set(t,s),d=(t,e,s,r)=>(Ot(t,e,"write to private field"),r?r.call(t,s):e.set(t,s),s),C=(t,e,s)=>(Ot(t,e,"access private method"),s);var kt=(t,e,s,r)=>({set _(i){d(t,e,i,s)},get _(){return a(t,e,r)}});import{r as m,a as Rr,u as Or}from"./vendor-http-Bngxmn39.js";var Pt={exports:{}},tt={};/**
 * @license React
 * react-jsx-runtime.production.js
 *
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 *
 * This source code is licensed under the MIT license found in the
 * LICENSE file in the root directory of this source tree.
 */var gs;function Pr(){if(gs)return tt;gs=1;var t=Symbol.for("react.transitional.element"),e=Symbol.for("react.fragment");function s(r,i,n){var o=null;if(n!==void 0&&(o=""+n),i.key!==void 0&&(o=""+i.key),"key"in i){n={};for(var l in i)l!=="key"&&(n[l]=i[l])}else n=i;return i=n.ref,{$$typeof:t,type:r,key:o,ref:i!==void 0?i:null,props:n}}return tt.Fragment=e,tt.jsx=s,tt.jsxs=s,tt}var xs;function $r(){return xs||(xs=1,Pt.exports=Pr()),Pt.exports}var c=$r(),De=class{constructor(){this.listeners=new Set,this.subscribe=this.subscribe.bind(this)}subscribe(t){return this.listeners.add(t),this.onSubscribe(),()=>{this.listeners.delete(t),this.onUnsubscribe()}}hasListeners(){return this.listeners.size>0}onSubscribe(){}onUnsubscribe(){}},_e,ye,qe,Ds,Tr=(Ds=class extends De{constructor(){super();x(this,_e);x(this,ye);x(this,qe);d(this,qe,e=>{if(typeof window<"u"&&window.addEventListener){const s=()=>e();return window.addEventListener("visibilitychange",s,!1),()=>{window.removeEventListener("visibilitychange",s)}}})}onSubscribe(){a(this,ye)||this.setEventListener(a(this,qe))}onUnsubscribe(){var e;this.hasListeners()||((e=a(this,ye))==null||e.call(this),d(this,ye,void 0))}setEventListener(e){var s;d(this,qe,e),(s=a(this,ye))==null||s.call(this),d(this,ye,e(r=>{typeof r=="boolean"?this.setFocused(r):this.onFocus()}))}setFocused(e){a(this,_e)!==e&&(d(this,_e,e),this.onFocus())}onFocus(){const e=this.isFocused();this.listeners.forEach(s=>{s(e)})}isFocused(){var e;return typeof a(this,_e)=="boolean"?a(this,_e):((e=globalThis.document)==null?void 0:e.visibilityState)!=="hidden"}},_e=new WeakMap,ye=new WeakMap,qe=new WeakMap,Ds),is=new Tr,Ar={setTimeout:(t,e)=>setTimeout(t,e),clearTimeout:t=>clearTimeout(t),setInterval:(t,e)=>setInterval(t,e),clearInterval:t=>clearInterval(t)},me,as,Qs,Ir=(Qs=class{constructor(){x(this,me,Ar);x(this,as,!1)}setTimeoutProvider(t){d(this,me,t)}setTimeout(t,e){return a(this,me).setTimeout(t,e)}clearTimeout(t){a(this,me).clearTimeout(t)}setInterval(t,e){return a(this,me).setInterval(t,e)}clearInterval(t){a(this,me).clearInterval(t)}},me=new WeakMap,as=new WeakMap,Qs),Se=new Ir;function Fr(t){setTimeout(t,0)}var Dr=typeof window>"u"||"Deno"in globalThis;function L(){}function Qr(t,e){return typeof t=="function"?t(e):t}function Tt(t){return typeof t=="number"&&t>=0&&t!==1/0}function Zs(t,e){return Math.max(t+(e||0)-Date.now(),0)}function je(t,e){return typeof t=="function"?t(e):t}function Z(t,e){return typeof t=="function"?t(e):t}function vs(t,e){const{type:s="all",exact:r,fetchStatus:i,predicate:n,queryKey:o,stale:l}=t;if(o){if(r){if(e.queryHash!==ns(o,e.options))return!1}else if(!at(e.queryKey,o))return!1}if(s!=="all"){const u=e.isActive();if(s==="active"&&!u||s==="inactive"&&u)return!1}return!(typeof l=="boolean"&&e.isStale()!==l||i&&i!==e.state.fetchStatus||n&&!n(e))}function bs(t,e){const{exact:s,status:r,predicate:i,mutationKey:n}=t;if(n){if(!e.options.mutationKey)return!1;if(s){if(Fe(e.options.mutationKey)!==Fe(n))return!1}else if(!at(e.options.mutationKey,n))return!1}return!(r&&e.state.status!==r||i&&!i(e))}function ns(t,e){return((e==null?void 0:e.queryKeyHashFn)||Fe)(t)}function Fe(t){return JSON.stringify(t,(e,s)=>At(s)?Object.keys(s).sort().reduce((r,i)=>(r[i]=s[i],r),{}):s)}function at(t,e){return t===e?!0:typeof t!=typeof e?!1:t&&e&&typeof t=="object"&&typeof e=="object"?Object.keys(e).every(s=>at(t[s],e[s])):!1}var qr=Object.prototype.hasOwnProperty;function os(t,e,s=0){if(t===e)return t;if(s>500)return e;const r=ks(t)&&ks(e);if(!r&&!(At(t)&&At(e)))return e;const n=(r?t:Object.keys(t)).length,o=r?e:Object.keys(e),l=o.length,u=r?new Array(l):{};let p=0;for(let y=0;y<l;y++){const h=r?y:o[y],b=t[h],g=e[h];if(b===g){u[h]=b,(r?y<n:qr.call(t,h))&&p++;continue}if(b===null||g===null||typeof b!="object"||typeof g!="object"){u[h]=g;continue}const N=os(b,g,s+1);u[h]=N,N===b&&p++}return n===l&&p===n?t:u}function it(t,e){if(!e||Object.keys(t).length!==Object.keys(e).length)return!1;for(const s in t)if(t[s]!==e[s])return!1;return!0}function ks(t){return Array.isArray(t)&&t.length===Object.keys(t).length}function At(t){if(!ws(t))return!1;const e=t.constructor;if(e===void 0)return!0;const s=e.prototype;return!(!ws(s)||!s.hasOwnProperty("isPrototypeOf")||Object.getPrototypeOf(t)!==Object.prototype)}function ws(t){return Object.prototype.toString.call(t)==="[object Object]"}function Lr(t){return new Promise(e=>{Se.setTimeout(e,t)})}function It(t,e,s){return typeof s.structuralSharing=="function"?s.structuralSharing(t,e):s.structuralSharing!==!1?os(t,e):e}function zr(t,e,s=0){const r=[...t,e];return s&&r.length>s?r.slice(1):r}function Ur(t,e,s=0){const r=[e,...t];return s&&r.length>s?r.slice(0,-1):r}var cs=Symbol();function Ws(t,e){return!t.queryFn&&(e!=null&&e.initialPromise)?()=>e.initialPromise:!t.queryFn||t.queryFn===cs?()=>Promise.reject(new Error(`Missing queryFn: '${t.queryHash}'`)):t.queryFn}function ls(t,e){return typeof t=="function"?t(...e):!!t}function Hr(t,e,s){let r=!1,i;return Object.defineProperty(t,"signal",{enumerable:!0,get:()=>(i??(i=e()),r||(r=!0,i.aborted?s():i.addEventListener("abort",s,{once:!0})),i)}),t}var nt=(()=>{let t=()=>Dr;return{isServer(){return t()},setIsServer(e){t=e}}})();function Ft(){let t,e;const s=new Promise((i,n)=>{t=i,e=n});s.status="pending",s.catch(()=>{});function r(i){Object.assign(s,i),delete s.resolve,delete s.reject}return s.resolve=i=>{r({status:"fulfilled",value:i}),t(i)},s.reject=i=>{r({status:"rejected",reason:i}),e(i)},s}var Kr=Fr;function Vr(){let t=[],e=0,s=l=>{l()},r=l=>{l()},i=Kr;const n=l=>{e?t.push(l):i(()=>{s(l)})},o=()=>{const l=t;t=[],l.length&&i(()=>{r(()=>{l.forEach(u=>{s(u)})})})};return{batch:l=>{let u;e++;try{u=l()}finally{e--,e||o()}return u},batchCalls:l=>(...u)=>{n(()=>{l(...u)})},schedule:n,setNotifyFunction:l=>{s=l},setBatchNotifyFunction:l=>{r=l},setScheduler:l=>{i=l}}}var T=Vr(),Le,ge,ze,qs,Br=(qs=class extends De{constructor(){super();x(this,Le,!0);x(this,ge);x(this,ze);d(this,ze,e=>{if(typeof window<"u"&&window.addEventListener){const s=()=>e(!0),r=()=>e(!1);return window.addEventListener("online",s,!1),window.addEventListener("offline",r,!1),()=>{window.removeEventListener("online",s),window.removeEventListener("offline",r)}}})}onSubscribe(){a(this,ge)||this.setEventListener(a(this,ze))}onUnsubscribe(){var e;this.hasListeners()||((e=a(this,ge))==null||e.call(this),d(this,ge,void 0))}setEventListener(e){var s;d(this,ze,e),(s=a(this,ge))==null||s.call(this),d(this,ge,e(this.setOnline.bind(this)))}setOnline(e){a(this,Le)!==e&&(d(this,Le,e),this.listeners.forEach(r=>{r(e)}))}isOnline(){return a(this,Le)}},Le=new WeakMap,ge=new WeakMap,ze=new WeakMap,qs),Nt=new Br;function Gr(t){return Math.min(1e3*2**t,3e4)}function Ys(t){return(t??"online")==="online"?Nt.isOnline():!0}var Dt=class extends Error{constructor(t){super("CancelledError"),this.revert=t==null?void 0:t.revert,this.silent=t==null?void 0:t.silent}};function Xs(t){let e=!1,s=0,r;const i=Ft(),n=()=>i.status!=="pending",o=w=>{var j;if(!n()){const S=new Dt(w);b(S),(j=t.onCancel)==null||j.call(t,S)}},l=()=>{e=!0},u=()=>{e=!1},p=()=>is.isFocused()&&(t.networkMode==="always"||Nt.isOnline())&&t.canRun(),y=()=>Ys(t.networkMode)&&t.canRun(),h=w=>{n()||(r==null||r(),i.resolve(w))},b=w=>{n()||(r==null||r(),i.reject(w))},g=()=>new Promise(w=>{var j;r=S=>{(n()||p())&&w(S)},(j=t.onPause)==null||j.call(t)}).then(()=>{var w;r=void 0,n()||(w=t.onContinue)==null||w.call(t)}),N=()=>{if(n())return;let w;const j=s===0?t.initialPromise:void 0;try{w=j??t.fn()}catch(S){w=Promise.reject(S)}Promise.resolve(w).then(h).catch(S=>{var P;if(n())return;const O=t.retry??(nt.isServer()?0:3),k=t.retryDelay??Gr,M=typeof k=="function"?k(s,S):k,I=O===!0||typeof O=="number"&&s<O||typeof O=="function"&&O(s,S);if(e||!I){b(S);return}s++,(P=t.onFail)==null||P.call(t,s,S),Lr(M).then(()=>p()?void 0:g()).then(()=>{e?b(S):N()})})};return{promise:i,status:()=>i.status,cancel:o,continue:()=>(r==null||r(),i),cancelRetry:l,continueRetry:u,canStart:y,start:()=>(y()?N():g().then(N),i)}}var Ee,Ls,er=(Ls=class{constructor(){x(this,Ee)}destroy(){this.clearGcTimeout()}scheduleGc(){this.clearGcTimeout(),Tt(this.gcTime)&&d(this,Ee,Se.setTimeout(()=>{this.optionalRemove()},this.gcTime))}updateGcTime(t){this.gcTime=Math.max(this.gcTime||0,t??(nt.isServer()?1/0:300*1e3))}clearGcTimeout(){a(this,Ee)&&(Se.clearTimeout(a(this,Ee)),d(this,Ee,void 0))}},Ee=new WeakMap,Ls),Re,Ue,G,Oe,D,ot,Pe,B,tr,ae,zs,Jr=(zs=class extends er{constructor(e){super();x(this,B);x(this,Re);x(this,Ue);x(this,G);x(this,Oe);x(this,D);x(this,ot);x(this,Pe);d(this,Pe,!1),d(this,ot,e.defaultOptions),this.setOptions(e.options),this.observers=[],d(this,Oe,e.client),d(this,G,a(this,Oe).getQueryCache()),this.queryKey=e.queryKey,this.queryHash=e.queryHash,d(this,Re,Cs(this.options)),this.state=e.state??a(this,Re),this.scheduleGc()}get meta(){return this.options.meta}get promise(){var e;return(e=a(this,D))==null?void 0:e.promise}setOptions(e){if(this.options={...a(this,ot),...e},this.updateGcTime(this.options.gcTime),this.state&&this.state.data===void 0){const s=Cs(this.options);s.data!==void 0&&(this.setState(Ms(s.data,s.dataUpdatedAt)),d(this,Re,s))}}optionalRemove(){!this.observers.length&&this.state.fetchStatus==="idle"&&a(this,G).remove(this)}setData(e,s){const r=It(this.state.data,e,this.options);return C(this,B,ae).call(this,{data:r,type:"success",dataUpdatedAt:s==null?void 0:s.updatedAt,manual:s==null?void 0:s.manual}),r}setState(e,s){C(this,B,ae).call(this,{type:"setState",state:e,setStateOptions:s})}cancel(e){var r,i;const s=(r=a(this,D))==null?void 0:r.promise;return(i=a(this,D))==null||i.cancel(e),s?s.then(L).catch(L):Promise.resolve()}destroy(){super.destroy(),this.cancel({silent:!0})}get resetState(){return a(this,Re)}reset(){this.destroy(),this.setState(this.resetState)}isActive(){return this.observers.some(e=>Z(e.options.enabled,this)!==!1)}isDisabled(){return this.getObserversCount()>0?!this.isActive():this.options.queryFn===cs||!this.isFetched()}isFetched(){return this.state.dataUpdateCount+this.state.errorUpdateCount>0}isStatic(){return this.getObserversCount()>0?this.observers.some(e=>je(e.options.staleTime,this)==="static"):!1}isStale(){return this.getObserversCount()>0?this.observers.some(e=>e.getCurrentResult().isStale):this.state.data===void 0||this.state.isInvalidated}isStaleByTime(e=0){return this.state.data===void 0?!0:e==="static"?!1:this.state.isInvalidated?!0:!Zs(this.state.dataUpdatedAt,e)}onFocus(){var s;const e=this.observers.find(r=>r.shouldFetchOnWindowFocus());e==null||e.refetch({cancelRefetch:!1}),(s=a(this,D))==null||s.continue()}onOnline(){var s;const e=this.observers.find(r=>r.shouldFetchOnReconnect());e==null||e.refetch({cancelRefetch:!1}),(s=a(this,D))==null||s.continue()}addObserver(e){this.observers.includes(e)||(this.observers.push(e),this.clearGcTimeout(),a(this,G).notify({type:"observerAdded",query:this,observer:e}))}removeObserver(e){this.observers.includes(e)&&(this.observers=this.observers.filter(s=>s!==e),this.observers.length||(a(this,D)&&(a(this,Pe)||C(this,B,tr).call(this)?a(this,D).cancel({revert:!0}):a(this,D).cancelRetry()),this.scheduleGc()),a(this,G).notify({type:"observerRemoved",query:this,observer:e}))}getObserversCount(){return this.observers.length}invalidate(){this.state.isInvalidated||C(this,B,ae).call(this,{type:"invalidate"})}async fetch(e,s){var u,p,y,h,b,g,N,w,j,S,O,k;if(this.state.fetchStatus!=="idle"&&((u=a(this,D))==null?void 0:u.status())!=="rejected"){if(this.state.data!==void 0&&(s!=null&&s.cancelRefetch))this.cancel({silent:!0});else if(a(this,D))return a(this,D).continueRetry(),a(this,D).promise}if(e&&this.setOptions(e),!this.options.queryFn){const M=this.observers.find(I=>I.options.queryFn);M&&this.setOptions(M.options)}const r=new AbortController,i=M=>{Object.defineProperty(M,"signal",{enumerable:!0,get:()=>(d(this,Pe,!0),r.signal)})},n=()=>{const M=Ws(this.options,s),P=(()=>{const K={client:a(this,Oe),queryKey:this.queryKey,meta:this.meta};return i(K),K})();return d(this,Pe,!1),this.options.persister?this.options.persister(M,P,this):M(P)},l=(()=>{const M={fetchOptions:s,options:this.options,queryKey:this.queryKey,client:a(this,Oe),state:this.state,fetchFn:n};return i(M),M})();(p=this.options.behavior)==null||p.onFetch(l,this),d(this,Ue,this.state),(this.state.fetchStatus==="idle"||this.state.fetchMeta!==((y=l.fetchOptions)==null?void 0:y.meta))&&C(this,B,ae).call(this,{type:"fetch",meta:(h=l.fetchOptions)==null?void 0:h.meta}),d(this,D,Xs({initialPromise:s==null?void 0:s.initialPromise,fn:l.fetchFn,onCancel:M=>{M instanceof Dt&&M.revert&&this.setState({...a(this,Ue),fetchStatus:"idle"}),r.abort()},onFail:(M,I)=>{C(this,B,ae).call(this,{type:"failed",failureCount:M,error:I})},onPause:()=>{C(this,B,ae).call(this,{type:"pause"})},onContinue:()=>{C(this,B,ae).call(this,{type:"continue"})},retry:l.options.retry,retryDelay:l.options.retryDelay,networkMode:l.options.networkMode,canRun:()=>!0}));try{const M=await a(this,D).start();if(M===void 0)throw new Error(`${this.queryHash} data is undefined`);return this.setData(M),(g=(b=a(this,G).config).onSuccess)==null||g.call(b,M,this),(w=(N=a(this,G).config).onSettled)==null||w.call(N,M,this.state.error,this),M}catch(M){if(M instanceof Dt){if(M.silent)return a(this,D).promise;if(M.revert){if(this.state.data===void 0)throw M;return this.state.data}}throw C(this,B,ae).call(this,{type:"error",error:M}),(S=(j=a(this,G).config).onError)==null||S.call(j,M,this),(k=(O=a(this,G).config).onSettled)==null||k.call(O,this.state.data,M,this),M}finally{this.scheduleGc()}}},Re=new WeakMap,Ue=new WeakMap,G=new WeakMap,Oe=new WeakMap,D=new WeakMap,ot=new WeakMap,Pe=new WeakMap,B=new WeakSet,tr=function(){return this.state.fetchStatus==="paused"&&this.state.status==="pending"},ae=function(e){const s=r=>{switch(e.type){case"failed":return{...r,fetchFailureCount:e.failureCount,fetchFailureReason:e.error};case"pause":return{...r,fetchStatus:"paused"};case"continue":return{...r,fetchStatus:"fetching"};case"fetch":return{...r,...sr(r.data,this.options),fetchMeta:e.meta??null};case"success":const i={...r,...Ms(e.data,e.dataUpdatedAt),dataUpdateCount:r.dataUpdateCount+1,...!e.manual&&{fetchStatus:"idle",fetchFailureCount:0,fetchFailureReason:null}};return d(this,Ue,e.manual?i:void 0),i;case"error":const n=e.error;return{...r,error:n,errorUpdateCount:r.errorUpdateCount+1,errorUpdatedAt:Date.now(),fetchFailureCount:r.fetchFailureCount+1,fetchFailureReason:n,fetchStatus:"idle",status:"error",isInvalidated:!0};case"invalidate":return{...r,isInvalidated:!0};case"setState":return{...r,...e.state}}};this.state=s(this.state),T.batch(()=>{this.observers.forEach(r=>{r.onQueryUpdate()}),a(this,G).notify({query:this,type:"updated",action:e})})},zs);function sr(t,e){return{fetchFailureCount:0,fetchFailureReason:null,fetchStatus:Ys(e.networkMode)?"fetching":"paused",...t===void 0&&{error:null,status:"pending"}}}function Ms(t,e){return{data:t,dataUpdatedAt:e??Date.now(),error:null,isInvalidated:!1,status:"success"}}function Cs(t){const e=typeof t.initialData=="function"?t.initialData():t.initialData,s=e!==void 0,r=s?typeof t.initialDataUpdatedAt=="function"?t.initialDataUpdatedAt():t.initialDataUpdatedAt:0;return{data:e,dataUpdateCount:0,dataUpdatedAt:s?r??Date.now():0,error:null,errorUpdateCount:0,errorUpdatedAt:0,fetchFailureCount:0,fetchFailureReason:null,fetchMeta:null,isInvalidated:!1,status:s?"success":"pending",fetchStatus:"idle"}}var U,_,ct,z,$e,He,ie,xe,lt,Ke,Ve,Te,Ae,ve,Be,E,rt,Qt,qt,Lt,zt,Ut,Ht,Kt,rr,Us,hs=(Us=class extends De{constructor(e,s){super();x(this,E);x(this,U);x(this,_);x(this,ct);x(this,z);x(this,$e);x(this,He);x(this,ie);x(this,xe);x(this,lt);x(this,Ke);x(this,Ve);x(this,Te);x(this,Ae);x(this,ve);x(this,Be,new Set);this.options=s,d(this,U,e),d(this,xe,null),d(this,ie,Ft()),this.bindMethods(),this.setOptions(s)}bindMethods(){this.refetch=this.refetch.bind(this)}onSubscribe(){this.listeners.size===1&&(a(this,_).addObserver(this),js(a(this,_),this.options)?C(this,E,rt).call(this):this.updateResult(),C(this,E,zt).call(this))}onUnsubscribe(){this.hasListeners()||this.destroy()}shouldFetchOnReconnect(){return Vt(a(this,_),this.options,this.options.refetchOnReconnect)}shouldFetchOnWindowFocus(){return Vt(a(this,_),this.options,this.options.refetchOnWindowFocus)}destroy(){this.listeners=new Set,C(this,E,Ut).call(this),C(this,E,Ht).call(this),a(this,_).removeObserver(this)}setOptions(e){const s=this.options,r=a(this,_);if(this.options=a(this,U).defaultQueryOptions(e),this.options.enabled!==void 0&&typeof this.options.enabled!="boolean"&&typeof this.options.enabled!="function"&&typeof Z(this.options.enabled,a(this,_))!="boolean")throw new Error("Expected enabled to be a boolean or a callback that returns a boolean");C(this,E,Kt).call(this),a(this,_).setOptions(this.options),s._defaulted&&!it(this.options,s)&&a(this,U).getQueryCache().notify({type:"observerOptionsUpdated",query:a(this,_),observer:this});const i=this.hasListeners();i&&Ns(a(this,_),r,this.options,s)&&C(this,E,rt).call(this),this.updateResult(),i&&(a(this,_)!==r||Z(this.options.enabled,a(this,_))!==Z(s.enabled,a(this,_))||je(this.options.staleTime,a(this,_))!==je(s.staleTime,a(this,_)))&&C(this,E,Qt).call(this);const n=C(this,E,qt).call(this);i&&(a(this,_)!==r||Z(this.options.enabled,a(this,_))!==Z(s.enabled,a(this,_))||n!==a(this,ve))&&C(this,E,Lt).call(this,n)}getOptimisticResult(e){const s=a(this,U).getQueryCache().build(a(this,U),e),r=this.createResult(s,e);return Wr(this,r)&&(d(this,z,r),d(this,He,this.options),d(this,$e,a(this,_).state)),r}getCurrentResult(){return a(this,z)}trackResult(e,s){return new Proxy(e,{get:(r,i)=>(this.trackProp(i),s==null||s(i),i==="promise"&&(this.trackProp("data"),!this.options.experimental_prefetchInRender&&a(this,ie).status==="pending"&&a(this,ie).reject(new Error("experimental_prefetchInRender feature flag is not enabled"))),Reflect.get(r,i))})}trackProp(e){a(this,Be).add(e)}getCurrentQuery(){return a(this,_)}refetch({...e}={}){return this.fetch({...e})}fetchOptimistic(e){const s=a(this,U).defaultQueryOptions(e),r=a(this,U).getQueryCache().build(a(this,U),s);return r.fetch().then(()=>this.createResult(r,s))}fetch(e){return C(this,E,rt).call(this,{...e,cancelRefetch:e.cancelRefetch??!0}).then(()=>(this.updateResult(),a(this,z)))}createResult(e,s){var R;const r=a(this,_),i=this.options,n=a(this,z),o=a(this,$e),l=a(this,He),p=e!==r?e.state:a(this,ct),{state:y}=e;let h={...y},b=!1,g;if(s._optimisticResults){const A=this.hasListeners(),de=!A&&js(e,s),xt=A&&Ns(e,r,s,i);(de||xt)&&(h={...h,...sr(y.data,e.options)}),s._optimisticResults==="isRestoring"&&(h.fetchStatus="idle")}let{error:N,errorUpdatedAt:w,status:j}=h;g=h.data;let S=!1;if(s.placeholderData!==void 0&&g===void 0&&j==="pending"){let A;n!=null&&n.isPlaceholderData&&s.placeholderData===(l==null?void 0:l.placeholderData)?(A=n.data,S=!0):A=typeof s.placeholderData=="function"?s.placeholderData((R=a(this,Ve))==null?void 0:R.state.data,a(this,Ve)):s.placeholderData,A!==void 0&&(j="success",g=It(n==null?void 0:n.data,A,s),b=!0)}if(s.select&&g!==void 0&&!S)if(n&&g===(o==null?void 0:o.data)&&s.select===a(this,lt))g=a(this,Ke);else try{d(this,lt,s.select),g=s.select(g),g=It(n==null?void 0:n.data,g,s),d(this,Ke,g),d(this,xe,null)}catch(A){d(this,xe,A)}a(this,xe)&&(N=a(this,xe),g=a(this,Ke),w=Date.now(),j="error");const O=h.fetchStatus==="fetching",k=j==="pending",M=j==="error",I=k&&O,P=g!==void 0,v={status:j,fetchStatus:h.fetchStatus,isPending:k,isSuccess:j==="success",isError:M,isInitialLoading:I,isLoading:I,data:g,dataUpdatedAt:h.dataUpdatedAt,error:N,errorUpdatedAt:w,failureCount:h.fetchFailureCount,failureReason:h.fetchFailureReason,errorUpdateCount:h.errorUpdateCount,isFetched:e.isFetched(),isFetchedAfterMount:h.dataUpdateCount>p.dataUpdateCount||h.errorUpdateCount>p.errorUpdateCount,isFetching:O,isRefetching:O&&!k,isLoadingError:M&&!P,isPaused:h.fetchStatus==="paused",isPlaceholderData:b,isRefetchError:M&&P,isStale:us(e,s),refetch:this.refetch,promise:a(this,ie),isEnabled:Z(s.enabled,e)!==!1};if(this.options.experimental_prefetchInRender){const A=v.data!==void 0,de=v.status==="error"&&!A,xt=bt=>{de?bt.reject(v.error):A&&bt.resolve(v.data)},ys=()=>{const bt=d(this,ie,v.promise=Ft());xt(bt)},vt=a(this,ie);switch(vt.status){case"pending":e.queryHash===r.queryHash&&xt(vt);break;case"fulfilled":(de||v.data!==vt.value)&&ys();break;case"rejected":(!de||v.error!==vt.reason)&&ys();break}}return v}updateResult(){const e=a(this,z),s=this.createResult(a(this,_),this.options);if(d(this,$e,a(this,_).state),d(this,He,this.options),a(this,$e).data!==void 0&&d(this,Ve,a(this,_)),it(s,e))return;d(this,z,s);const r=()=>{if(!e)return!0;const{notifyOnChangeProps:i}=this.options,n=typeof i=="function"?i():i;if(n==="all"||!n&&!a(this,Be).size)return!0;const o=new Set(n??a(this,Be));return this.options.throwOnError&&o.add("error"),Object.keys(a(this,z)).some(l=>{const u=l;return a(this,z)[u]!==e[u]&&o.has(u)})};C(this,E,rr).call(this,{listeners:r()})}onQueryUpdate(){this.updateResult(),this.hasListeners()&&C(this,E,zt).call(this)}},U=new WeakMap,_=new WeakMap,ct=new WeakMap,z=new WeakMap,$e=new WeakMap,He=new WeakMap,ie=new WeakMap,xe=new WeakMap,lt=new WeakMap,Ke=new WeakMap,Ve=new WeakMap,Te=new WeakMap,Ae=new WeakMap,ve=new WeakMap,Be=new WeakMap,E=new WeakSet,rt=function(e){C(this,E,Kt).call(this);let s=a(this,_).fetch(this.options,e);return e!=null&&e.throwOnError||(s=s.catch(L)),s},Qt=function(){C(this,E,Ut).call(this);const e=je(this.options.staleTime,a(this,_));if(nt.isServer()||a(this,z).isStale||!Tt(e))return;const r=Zs(a(this,z).dataUpdatedAt,e)+1;d(this,Te,Se.setTimeout(()=>{a(this,z).isStale||this.updateResult()},r))},qt=function(){return(typeof this.options.refetchInterval=="function"?this.options.refetchInterval(a(this,_)):this.options.refetchInterval)??!1},Lt=function(e){C(this,E,Ht).call(this),d(this,ve,e),!(nt.isServer()||Z(this.options.enabled,a(this,_))===!1||!Tt(a(this,ve))||a(this,ve)===0)&&d(this,Ae,Se.setInterval(()=>{(this.options.refetchIntervalInBackground||is.isFocused())&&C(this,E,rt).call(this)},a(this,ve)))},zt=function(){C(this,E,Qt).call(this),C(this,E,Lt).call(this,C(this,E,qt).call(this))},Ut=function(){a(this,Te)&&(Se.clearTimeout(a(this,Te)),d(this,Te,void 0))},Ht=function(){a(this,Ae)&&(Se.clearInterval(a(this,Ae)),d(this,Ae,void 0))},Kt=function(){const e=a(this,U).getQueryCache().build(a(this,U),this.options);if(e===a(this,_))return;const s=a(this,_);d(this,_,e),d(this,ct,e.state),this.hasListeners()&&(s==null||s.removeObserver(this),e.addObserver(this))},rr=function(e){T.batch(()=>{e.listeners&&this.listeners.forEach(s=>{s(a(this,z))}),a(this,U).getQueryCache().notify({query:a(this,_),type:"observerResultsUpdated"})})},Us);function Zr(t,e){return Z(e.enabled,t)!==!1&&t.state.data===void 0&&!(t.state.status==="error"&&e.retryOnMount===!1)}function js(t,e){return Zr(t,e)||t.state.data!==void 0&&Vt(t,e,e.refetchOnMount)}function Vt(t,e,s){if(Z(e.enabled,t)!==!1&&je(e.staleTime,t)!=="static"){const r=typeof s=="function"?s(t):s;return r==="always"||r!==!1&&us(t,e)}return!1}function Ns(t,e,s,r){return(t!==e||Z(r.enabled,t)===!1)&&(!s.suspense||t.state.status!=="error")&&us(t,s)}function us(t,e){return Z(e.enabled,t)!==!1&&t.isStaleByTime(je(e.staleTime,t))}function Wr(t,e){return!it(t.getCurrentResult(),e)}function Ss(t){return{onFetch:(e,s)=>{var y,h,b,g,N;const r=e.options,i=(b=(h=(y=e.fetchOptions)==null?void 0:y.meta)==null?void 0:h.fetchMore)==null?void 0:b.direction,n=((g=e.state.data)==null?void 0:g.pages)||[],o=((N=e.state.data)==null?void 0:N.pageParams)||[];let l={pages:[],pageParams:[]},u=0;const p=async()=>{let w=!1;const j=k=>{Hr(k,()=>e.signal,()=>w=!0)},S=Ws(e.options,e.fetchOptions),O=async(k,M,I)=>{if(w)return Promise.reject();if(M==null&&k.pages.length)return Promise.resolve(k);const K=(()=>{const de={client:e.client,queryKey:e.queryKey,pageParam:M,direction:I?"backward":"forward",meta:e.options.meta};return j(de),de})(),v=await S(K),{maxPages:R}=e.options,A=I?Ur:zr;return{pages:A(k.pages,v,R),pageParams:A(k.pageParams,M,R)}};if(i&&n.length){const k=i==="backward",M=k?Yr:_s,I={pages:n,pageParams:o},P=M(r,I);l=await O(I,P,k)}else{const k=t??n.length;do{const M=u===0?o[0]??r.initialPageParam:_s(r,l);if(u>0&&M==null)break;l=await O(l,M),u++}while(u<k)}return l};e.options.persister?e.fetchFn=()=>{var w,j;return(j=(w=e.options).persister)==null?void 0:j.call(w,p,{client:e.client,queryKey:e.queryKey,meta:e.options.meta,signal:e.signal},s)}:e.fetchFn=p}}}function _s(t,{pages:e,pageParams:s}){const r=e.length-1;return e.length>0?t.getNextPageParam(e[r],e,s[r],s):void 0}function Yr(t,{pages:e,pageParams:s}){var r;return e.length>0?(r=t.getPreviousPageParam)==null?void 0:r.call(t,e[0],e,s[0],s):void 0}var ht,X,q,Ie,ee,pe,Hs,Xr=(Hs=class extends er{constructor(e){super();x(this,ee);x(this,ht);x(this,X);x(this,q);x(this,Ie);d(this,ht,e.client),this.mutationId=e.mutationId,d(this,q,e.mutationCache),d(this,X,[]),this.state=e.state||ar(),this.setOptions(e.options),this.scheduleGc()}setOptions(e){this.options=e,this.updateGcTime(this.options.gcTime)}get meta(){return this.options.meta}addObserver(e){a(this,X).includes(e)||(a(this,X).push(e),this.clearGcTimeout(),a(this,q).notify({type:"observerAdded",mutation:this,observer:e}))}removeObserver(e){d(this,X,a(this,X).filter(s=>s!==e)),this.scheduleGc(),a(this,q).notify({type:"observerRemoved",mutation:this,observer:e})}optionalRemove(){a(this,X).length||(this.state.status==="pending"?this.scheduleGc():a(this,q).remove(this))}continue(){var e;return((e=a(this,Ie))==null?void 0:e.continue())??this.execute(this.state.variables)}async execute(e){var o,l,u,p,y,h,b,g,N,w,j,S,O,k,M,I,P,K;const s=()=>{C(this,ee,pe).call(this,{type:"continue"})},r={client:a(this,ht),meta:this.options.meta,mutationKey:this.options.mutationKey};d(this,Ie,Xs({fn:()=>this.options.mutationFn?this.options.mutationFn(e,r):Promise.reject(new Error("No mutationFn found")),onFail:(v,R)=>{C(this,ee,pe).call(this,{type:"failed",failureCount:v,error:R})},onPause:()=>{C(this,ee,pe).call(this,{type:"pause"})},onContinue:s,retry:this.options.retry??0,retryDelay:this.options.retryDelay,networkMode:this.options.networkMode,canRun:()=>a(this,q).canRun(this)}));const i=this.state.status==="pending",n=!a(this,Ie).canStart();try{if(i)s();else{C(this,ee,pe).call(this,{type:"pending",variables:e,isPaused:n}),a(this,q).config.onMutate&&await a(this,q).config.onMutate(e,this,r);const R=await((l=(o=this.options).onMutate)==null?void 0:l.call(o,e,r));R!==this.state.context&&C(this,ee,pe).call(this,{type:"pending",context:R,variables:e,isPaused:n})}const v=await a(this,Ie).start();return await((p=(u=a(this,q).config).onSuccess)==null?void 0:p.call(u,v,e,this.state.context,this,r)),await((h=(y=this.options).onSuccess)==null?void 0:h.call(y,v,e,this.state.context,r)),await((g=(b=a(this,q).config).onSettled)==null?void 0:g.call(b,v,null,this.state.variables,this.state.context,this,r)),await((w=(N=this.options).onSettled)==null?void 0:w.call(N,v,null,e,this.state.context,r)),C(this,ee,pe).call(this,{type:"success",data:v}),v}catch(v){try{await((S=(j=a(this,q).config).onError)==null?void 0:S.call(j,v,e,this.state.context,this,r))}catch(R){Promise.reject(R)}try{await((k=(O=this.options).onError)==null?void 0:k.call(O,v,e,this.state.context,r))}catch(R){Promise.reject(R)}try{await((I=(M=a(this,q).config).onSettled)==null?void 0:I.call(M,void 0,v,this.state.variables,this.state.context,this,r))}catch(R){Promise.reject(R)}try{await((K=(P=this.options).onSettled)==null?void 0:K.call(P,void 0,v,e,this.state.context,r))}catch(R){Promise.reject(R)}throw C(this,ee,pe).call(this,{type:"error",error:v}),v}finally{a(this,q).runNext(this)}}},ht=new WeakMap,X=new WeakMap,q=new WeakMap,Ie=new WeakMap,ee=new WeakSet,pe=function(e){const s=r=>{switch(e.type){case"failed":return{...r,failureCount:e.failureCount,failureReason:e.error};case"pause":return{...r,isPaused:!0};case"continue":return{...r,isPaused:!1};case"pending":return{...r,context:e.context,data:void 0,failureCount:0,failureReason:null,error:null,isPaused:e.isPaused,status:"pending",variables:e.variables,submittedAt:Date.now()};case"success":return{...r,data:e.data,failureCount:0,failureReason:null,error:null,status:"success",isPaused:!1};case"error":return{...r,data:void 0,error:e.error,failureCount:r.failureCount+1,failureReason:e.error,isPaused:!1,status:"error"}}};this.state=s(this.state),T.batch(()=>{a(this,X).forEach(r=>{r.onMutationUpdate(e)}),a(this,q).notify({mutation:this,type:"updated",action:e})})},Hs);function ar(){return{context:void 0,data:void 0,error:null,failureCount:0,failureReason:null,isPaused:!1,status:"idle",variables:void 0,submittedAt:0}}var ne,W,ut,Ks,ea=(Ks=class extends De{constructor(e={}){super();x(this,ne);x(this,W);x(this,ut);this.config=e,d(this,ne,new Set),d(this,W,new Map),d(this,ut,0)}build(e,s,r){const i=new Xr({client:e,mutationCache:this,mutationId:++kt(this,ut)._,options:e.defaultMutationOptions(s),state:r});return this.add(i),i}add(e){a(this,ne).add(e);const s=wt(e);if(typeof s=="string"){const r=a(this,W).get(s);r?r.push(e):a(this,W).set(s,[e])}this.notify({type:"added",mutation:e})}remove(e){if(a(this,ne).delete(e)){const s=wt(e);if(typeof s=="string"){const r=a(this,W).get(s);if(r)if(r.length>1){const i=r.indexOf(e);i!==-1&&r.splice(i,1)}else r[0]===e&&a(this,W).delete(s)}}this.notify({type:"removed",mutation:e})}canRun(e){const s=wt(e);if(typeof s=="string"){const r=a(this,W).get(s),i=r==null?void 0:r.find(n=>n.state.status==="pending");return!i||i===e}else return!0}runNext(e){var r;const s=wt(e);if(typeof s=="string"){const i=(r=a(this,W).get(s))==null?void 0:r.find(n=>n!==e&&n.state.isPaused);return(i==null?void 0:i.continue())??Promise.resolve()}else return Promise.resolve()}clear(){T.batch(()=>{a(this,ne).forEach(e=>{this.notify({type:"removed",mutation:e})}),a(this,ne).clear(),a(this,W).clear()})}getAll(){return Array.from(a(this,ne))}find(e){const s={exact:!0,...e};return this.getAll().find(r=>bs(s,r))}findAll(e={}){return this.getAll().filter(s=>bs(e,s))}notify(e){T.batch(()=>{this.listeners.forEach(s=>{s(e)})})}resumePausedMutations(){const e=this.getAll().filter(s=>s.state.isPaused);return T.batch(()=>Promise.all(e.map(s=>s.continue().catch(L))))}},ne=new WeakMap,W=new WeakMap,ut=new WeakMap,Ks);function wt(t){var e;return(e=t.options.scope)==null?void 0:e.id}var oe,be,H,ce,he,Ct,Bt,Vs,ta=(Vs=class extends De{constructor(s,r){super();x(this,he);x(this,oe);x(this,be);x(this,H);x(this,ce);d(this,oe,s),this.setOptions(r),this.bindMethods(),C(this,he,Ct).call(this)}bindMethods(){this.mutate=this.mutate.bind(this),this.reset=this.reset.bind(this)}setOptions(s){var i;const r=this.options;this.options=a(this,oe).defaultMutationOptions(s),it(this.options,r)||a(this,oe).getMutationCache().notify({type:"observerOptionsUpdated",mutation:a(this,H),observer:this}),r!=null&&r.mutationKey&&this.options.mutationKey&&Fe(r.mutationKey)!==Fe(this.options.mutationKey)?this.reset():((i=a(this,H))==null?void 0:i.state.status)==="pending"&&a(this,H).setOptions(this.options)}onUnsubscribe(){var s;this.hasListeners()||(s=a(this,H))==null||s.removeObserver(this)}onMutationUpdate(s){C(this,he,Ct).call(this),C(this,he,Bt).call(this,s)}getCurrentResult(){return a(this,be)}reset(){var s;(s=a(this,H))==null||s.removeObserver(this),d(this,H,void 0),C(this,he,Ct).call(this),C(this,he,Bt).call(this)}mutate(s,r){var i;return d(this,ce,r),(i=a(this,H))==null||i.removeObserver(this),d(this,H,a(this,oe).getMutationCache().build(a(this,oe),this.options)),a(this,H).addObserver(this),a(this,H).execute(s)}},oe=new WeakMap,be=new WeakMap,H=new WeakMap,ce=new WeakMap,he=new WeakSet,Ct=function(){var r;const s=((r=a(this,H))==null?void 0:r.state)??ar();d(this,be,{...s,isPending:s.status==="pending",isSuccess:s.status==="success",isError:s.status==="error",isIdle:s.status==="idle",mutate:this.mutate,reset:this.reset})},Bt=function(s){T.batch(()=>{var r,i,n,o,l,u,p,y;if(a(this,ce)&&this.hasListeners()){const h=a(this,be).variables,b=a(this,be).context,g={client:a(this,oe),meta:this.options.meta,mutationKey:this.options.mutationKey};if((s==null?void 0:s.type)==="success"){try{(i=(r=a(this,ce)).onSuccess)==null||i.call(r,s.data,h,b,g)}catch(N){Promise.reject(N)}try{(o=(n=a(this,ce)).onSettled)==null||o.call(n,s.data,null,h,b,g)}catch(N){Promise.reject(N)}}else if((s==null?void 0:s.type)==="error"){try{(u=(l=a(this,ce)).onError)==null||u.call(l,s.error,h,b,g)}catch(N){Promise.reject(N)}try{(y=(p=a(this,ce)).onSettled)==null||y.call(p,void 0,s.error,h,b,g)}catch(N){Promise.reject(N)}}}this.listeners.forEach(h=>{h(a(this,be))})})},Vs);function Es(t,e){const s=new Set(e);return t.filter(r=>!s.has(r))}function sa(t,e,s){const r=t.slice(0);return r[e]=s,r}var Ge,V,Je,Ze,J,ke,dt,pt,ft,yt,Q,Gt,Jt,Zt,Wt,Yt,Bs,ra=(Bs=class extends De{constructor(e,s,r){super();x(this,Q);x(this,Ge);x(this,V);x(this,Je);x(this,Ze);x(this,J);x(this,ke);x(this,dt);x(this,pt);x(this,ft);x(this,yt,[]);d(this,Ge,e),d(this,Ze,r),d(this,Je,[]),d(this,J,[]),d(this,V,[]),this.setQueries(s)}onSubscribe(){this.listeners.size===1&&a(this,J).forEach(e=>{e.subscribe(s=>{C(this,Q,Wt).call(this,e,s)})})}onUnsubscribe(){this.listeners.size||this.destroy()}destroy(){this.listeners=new Set,a(this,J).forEach(e=>{e.destroy()})}setQueries(e,s){d(this,Je,e),d(this,Ze,s),T.batch(()=>{const r=a(this,J),i=C(this,Q,Zt).call(this,a(this,Je));i.forEach(h=>h.observer.setOptions(h.defaultedQueryOptions));const n=i.map(h=>h.observer),o=n.map(h=>h.getCurrentResult()),l=r.length!==n.length,u=n.some((h,b)=>h!==r[b]),p=l||u,y=p?!0:o.some((h,b)=>{const g=a(this,V)[b];return!g||!it(h,g)});!p&&!y||(p&&(d(this,yt,i),d(this,J,n)),d(this,V,o),this.hasListeners()&&(p&&(Es(r,n).forEach(h=>{h.destroy()}),Es(n,r).forEach(h=>{h.subscribe(b=>{C(this,Q,Wt).call(this,h,b)})})),C(this,Q,Yt).call(this)))})}getCurrentResult(){return a(this,V)}getQueries(){return a(this,J).map(e=>e.getCurrentQuery())}getObservers(){return a(this,J)}getOptimisticResult(e,s){const r=C(this,Q,Zt).call(this,e),i=r.map(o=>o.observer.getOptimisticResult(o.defaultedQueryOptions)),n=r.map(o=>o.defaultedQueryOptions.queryHash);return[i,o=>C(this,Q,Jt).call(this,o??i,s,n),()=>C(this,Q,Gt).call(this,i,r)]}},Ge=new WeakMap,V=new WeakMap,Je=new WeakMap,Ze=new WeakMap,J=new WeakMap,ke=new WeakMap,dt=new WeakMap,pt=new WeakMap,ft=new WeakMap,yt=new WeakMap,Q=new WeakSet,Gt=function(e,s){return s.map((r,i)=>{const n=e[i];return r.defaultedQueryOptions.notifyOnChangeProps?n:r.observer.trackResult(n,o=>{s.forEach(l=>{l.observer.trackProp(o)})})})},Jt=function(e,s,r){if(s){const i=a(this,ft),n=r!==void 0&&i!==void 0&&(i.length!==r.length||r.some((o,l)=>o!==i[l]));return(!a(this,ke)||a(this,V)!==a(this,pt)||n||s!==a(this,dt))&&(d(this,dt,s),d(this,pt,a(this,V)),r!==void 0&&d(this,ft,r),d(this,ke,os(a(this,ke),s(e)))),a(this,ke)}return e},Zt=function(e){const s=new Map;a(this,J).forEach(i=>{const n=i.options.queryHash;if(!n)return;const o=s.get(n);o?o.push(i):s.set(n,[i])});const r=[];return e.forEach(i=>{var u;const n=a(this,Ge).defaultQueryOptions(i),l=((u=s.get(n.queryHash))==null?void 0:u.shift())??new hs(a(this,Ge),n);r.push({defaultedQueryOptions:n,observer:l})}),r},Wt=function(e,s){const r=a(this,J).indexOf(e);r!==-1&&(d(this,V,sa(a(this,V),r,s)),C(this,Q,Yt).call(this))},Yt=function(){var e;if(this.hasListeners()){const s=a(this,ke),r=C(this,Q,Gt).call(this,a(this,V),a(this,yt)),i=C(this,Q,Jt).call(this,r,(e=a(this,Ze))==null?void 0:e.combine);s!==i&&T.batch(()=>{this.listeners.forEach(n=>{n(a(this,V))})})}},Bs),te,Gs,aa=(Gs=class extends De{constructor(e={}){super();x(this,te);this.config=e,d(this,te,new Map)}build(e,s,r){const i=s.queryKey,n=s.queryHash??ns(i,s);let o=this.get(n);return o||(o=new Jr({client:e,queryKey:i,queryHash:n,options:e.defaultQueryOptions(s),state:r,defaultOptions:e.getQueryDefaults(i)}),this.add(o)),o}add(e){a(this,te).has(e.queryHash)||(a(this,te).set(e.queryHash,e),this.notify({type:"added",query:e}))}remove(e){const s=a(this,te).get(e.queryHash);s&&(e.destroy(),s===e&&a(this,te).delete(e.queryHash),this.notify({type:"removed",query:e}))}clear(){T.batch(()=>{this.getAll().forEach(e=>{this.remove(e)})})}get(e){return a(this,te).get(e)}getAll(){return[...a(this,te).values()]}find(e){const s={exact:!0,...e};return this.getAll().find(r=>vs(s,r))}findAll(e={}){const s=this.getAll();return Object.keys(e).length>0?s.filter(r=>vs(e,r)):s}notify(e){T.batch(()=>{this.listeners.forEach(s=>{s(e)})})}onFocus(){T.batch(()=>{this.getAll().forEach(e=>{e.onFocus()})})}onOnline(){T.batch(()=>{this.getAll().forEach(e=>{e.onOnline()})})}},te=new WeakMap,Gs),$,we,Me,We,Ye,Ce,Xe,et,Js,Yn=(Js=class{constructor(t={}){x(this,$);x(this,we);x(this,Me);x(this,We);x(this,Ye);x(this,Ce);x(this,Xe);x(this,et);d(this,$,t.queryCache||new aa),d(this,we,t.mutationCache||new ea),d(this,Me,t.defaultOptions||{}),d(this,We,new Map),d(this,Ye,new Map),d(this,Ce,0)}mount(){kt(this,Ce)._++,a(this,Ce)===1&&(d(this,Xe,is.subscribe(async t=>{t&&(await this.resumePausedMutations(),a(this,$).onFocus())})),d(this,et,Nt.subscribe(async t=>{t&&(await this.resumePausedMutations(),a(this,$).onOnline())})))}unmount(){var t,e;kt(this,Ce)._--,a(this,Ce)===0&&((t=a(this,Xe))==null||t.call(this),d(this,Xe,void 0),(e=a(this,et))==null||e.call(this),d(this,et,void 0))}isFetching(t){return a(this,$).findAll({...t,fetchStatus:"fetching"}).length}isMutating(t){return a(this,we).findAll({...t,status:"pending"}).length}getQueryData(t){var s;const e=this.defaultQueryOptions({queryKey:t});return(s=a(this,$).get(e.queryHash))==null?void 0:s.state.data}ensureQueryData(t){const e=this.defaultQueryOptions(t),s=a(this,$).build(this,e),r=s.state.data;return r===void 0?this.fetchQuery(t):(t.revalidateIfStale&&s.isStaleByTime(je(e.staleTime,s))&&this.prefetchQuery(e),Promise.resolve(r))}getQueriesData(t){return a(this,$).findAll(t).map(({queryKey:e,state:s})=>{const r=s.data;return[e,r]})}setQueryData(t,e,s){const r=this.defaultQueryOptions({queryKey:t}),i=a(this,$).get(r.queryHash),n=i==null?void 0:i.state.data,o=Qr(e,n);if(o!==void 0)return a(this,$).build(this,r).setData(o,{...s,manual:!0})}setQueriesData(t,e,s){return T.batch(()=>a(this,$).findAll(t).map(({queryKey:r})=>[r,this.setQueryData(r,e,s)]))}getQueryState(t){var s;const e=this.defaultQueryOptions({queryKey:t});return(s=a(this,$).get(e.queryHash))==null?void 0:s.state}removeQueries(t){const e=a(this,$);T.batch(()=>{e.findAll(t).forEach(s=>{e.remove(s)})})}resetQueries(t,e){const s=a(this,$);return T.batch(()=>(s.findAll(t).forEach(r=>{r.reset()}),this.refetchQueries({type:"active",...t},e)))}cancelQueries(t,e={}){const s={revert:!0,...e},r=T.batch(()=>a(this,$).findAll(t).map(i=>i.cancel(s)));return Promise.all(r).then(L).catch(L)}invalidateQueries(t,e={}){return T.batch(()=>(a(this,$).findAll(t).forEach(s=>{s.invalidate()}),(t==null?void 0:t.refetchType)==="none"?Promise.resolve():this.refetchQueries({...t,type:(t==null?void 0:t.refetchType)??(t==null?void 0:t.type)??"active"},e)))}refetchQueries(t,e={}){const s={...e,cancelRefetch:e.cancelRefetch??!0},r=T.batch(()=>a(this,$).findAll(t).filter(i=>!i.isDisabled()&&!i.isStatic()).map(i=>{let n=i.fetch(void 0,s);return s.throwOnError||(n=n.catch(L)),i.state.fetchStatus==="paused"?Promise.resolve():n}));return Promise.all(r).then(L)}fetchQuery(t){const e=this.defaultQueryOptions(t);e.retry===void 0&&(e.retry=!1);const s=a(this,$).build(this,e);return s.isStaleByTime(je(e.staleTime,s))?s.fetch(e):Promise.resolve(s.state.data)}prefetchQuery(t){return this.fetchQuery(t).then(L).catch(L)}fetchInfiniteQuery(t){return t.behavior=Ss(t.pages),this.fetchQuery(t)}prefetchInfiniteQuery(t){return this.fetchInfiniteQuery(t).then(L).catch(L)}ensureInfiniteQueryData(t){return t.behavior=Ss(t.pages),this.ensureQueryData(t)}resumePausedMutations(){return Nt.isOnline()?a(this,we).resumePausedMutations():Promise.resolve()}getQueryCache(){return a(this,$)}getMutationCache(){return a(this,we)}getDefaultOptions(){return a(this,Me)}setDefaultOptions(t){d(this,Me,t)}setQueryDefaults(t,e){a(this,We).set(Fe(t),{queryKey:t,defaultOptions:e})}getQueryDefaults(t){const e=[...a(this,We).values()],s={};return e.forEach(r=>{at(t,r.queryKey)&&Object.assign(s,r.defaultOptions)}),s}setMutationDefaults(t,e){a(this,Ye).set(Fe(t),{mutationKey:t,defaultOptions:e})}getMutationDefaults(t){const e=[...a(this,Ye).values()],s={};return e.forEach(r=>{at(t,r.mutationKey)&&Object.assign(s,r.defaultOptions)}),s}defaultQueryOptions(t){if(t._defaulted)return t;const e={...a(this,Me).queries,...this.getQueryDefaults(t.queryKey),...t,_defaulted:!0};return e.queryHash||(e.queryHash=ns(e.queryKey,e)),e.refetchOnReconnect===void 0&&(e.refetchOnReconnect=e.networkMode!=="always"),e.throwOnError===void 0&&(e.throwOnError=!!e.suspense),!e.networkMode&&e.persister&&(e.networkMode="offlineFirst"),e.queryFn===cs&&(e.enabled=!1),e}defaultMutationOptions(t){return t!=null&&t._defaulted?t:{...a(this,Me).mutations,...(t==null?void 0:t.mutationKey)&&this.getMutationDefaults(t.mutationKey),...t,_defaulted:!0}}clear(){a(this,$).clear(),a(this,we).clear()}},$=new WeakMap,we=new WeakMap,Me=new WeakMap,We=new WeakMap,Ye=new WeakMap,Ce=new WeakMap,Xe=new WeakMap,et=new WeakMap,Js),ir=m.createContext(void 0),mt=t=>{const e=m.useContext(ir);if(!e)throw new Error("No QueryClient set, use QueryClientProvider to set one");return e},Xn=({client:t,children:e})=>(m.useEffect(()=>(t.mount(),()=>{t.unmount()}),[t]),c.jsx(ir.Provider,{value:t,children:e})),nr=m.createContext(!1),or=()=>m.useContext(nr);nr.Provider;function ia(){let t=!1;return{clearReset:()=>{t=!1},reset:()=>{t=!0},isReset:()=>t}}var na=m.createContext(ia()),cr=()=>m.useContext(na),lr=(t,e,s)=>{const r=s!=null&&s.state.error&&typeof t.throwOnError=="function"?ls(t.throwOnError,[s.state.error,s]):t.throwOnError;(t.suspense||t.experimental_prefetchInRender||r)&&(e.isReset()||(t.retryOnMount=!1))},hr=t=>{m.useEffect(()=>{t.clearReset()},[t])},ur=({result:t,errorResetBoundary:e,throwOnError:s,query:r,suspense:i})=>t.isError&&!e.isReset()&&!t.isFetching&&r&&(i&&t.data===void 0||ls(s,[t.error,r])),dr=t=>{if(t.suspense){const s=i=>i==="static"?i:Math.max(i??1e3,1e3),r=t.staleTime;t.staleTime=typeof r=="function"?(...i)=>s(r(...i)):s(r),typeof t.gcTime=="number"&&(t.gcTime=Math.max(t.gcTime,1e3))}},oa=(t,e)=>t.isLoading&&t.isFetching&&!e,Xt=(t,e)=>(t==null?void 0:t.suspense)&&e.isPending,es=(t,e,s)=>e.fetchOptimistic(t).catch(()=>{s.clearReset()});function eo({queries:t,...e},s){const r=mt(),i=or(),n=cr(),o=m.useMemo(()=>t.map(w=>{const j=r.defaultQueryOptions(w);return j._optimisticResults=i?"isRestoring":"optimistic",j}),[t,r,i]);o.forEach(w=>{dr(w);const j=r.getQueryCache().get(w.queryHash);lr(w,n,j)}),hr(n);const[l]=m.useState(()=>new ra(r,o,e)),[u,p,y]=l.getOptimisticResult(o,e.combine),h=!i&&e.subscribed!==!1;m.useSyncExternalStore(m.useCallback(w=>h?l.subscribe(T.batchCalls(w)):L,[l,h]),()=>l.getCurrentResult(),()=>l.getCurrentResult()),m.useEffect(()=>{l.setQueries(o,e)},[o,e,l]);const g=u.some((w,j)=>Xt(o[j],w))?u.flatMap((w,j)=>{const S=o[j];if(S&&Xt(S,w)){const O=new hs(r,S);return es(S,O,n)}return[]}):[];if(g.length>0)throw Promise.all(g);const N=u.find((w,j)=>{const S=o[j];return S&&ur({result:w,errorResetBoundary:n,throwOnError:S.throwOnError,query:r.getQueryCache().get(S.queryHash),suspense:S.suspense})});if(N!=null&&N.error)throw N.error;return p(y())}function ca(t,e,s){var b,g,N,w;const r=or(),i=cr(),n=mt(),o=n.defaultQueryOptions(t);(g=(b=n.getDefaultOptions().queries)==null?void 0:b._experimental_beforeQuery)==null||g.call(b,o);const l=n.getQueryCache().get(o.queryHash);o._optimisticResults=r?"isRestoring":"optimistic",dr(o),lr(o,i,l),hr(i);const u=!n.getQueryCache().get(o.queryHash),[p]=m.useState(()=>new e(n,o)),y=p.getOptimisticResult(o),h=!r&&t.subscribed!==!1;if(m.useSyncExternalStore(m.useCallback(j=>{const S=h?p.subscribe(T.batchCalls(j)):L;return p.updateResult(),S},[p,h]),()=>p.getCurrentResult(),()=>p.getCurrentResult()),m.useEffect(()=>{p.setOptions(o)},[o,p]),Xt(o,y))throw es(o,p,i);if(ur({result:y,errorResetBoundary:i,throwOnError:o.throwOnError,query:l,suspense:o.suspense}))throw y.error;if((w=(N=n.getDefaultOptions().queries)==null?void 0:N._experimental_afterQuery)==null||w.call(N,o,y),o.experimental_prefetchInRender&&!nt.isServer()&&oa(y,r)){const j=u?es(o,p,i):l==null?void 0:l.promise;j==null||j.catch(L).finally(()=>{p.updateResult()})}return o.notifyOnChangeProps?y:p.trackResult(y)}function la(t,e){return ca(t,hs)}function ds(t,e){const s=mt(),[r]=m.useState(()=>new ta(s,t));m.useEffect(()=>{r.setOptions(t)},[r,t]);const i=m.useSyncExternalStore(m.useCallback(o=>r.subscribe(T.batchCalls(o)),[r]),()=>r.getCurrentResult(),()=>r.getCurrentResult()),n=m.useCallback((o,l)=>{r.mutate(o,l).catch(L)},[r]);if(i.error&&ls(r.options.throwOnError,[i.error]))throw i.error;return{...i,mutate:n,mutateAsync:i.mutate}}let ha={data:""},ua=t=>{if(typeof window=="object"){let e=(t?t.querySelector("#_goober"):window._goober)||Object.assign(document.createElement("style"),{innerHTML:" ",id:"_goober"});return e.nonce=window.__nonce__,e.parentNode||(t||document.head).appendChild(e),e.firstChild}return t||ha},da=/(?:([\u0080-\uFFFF\w-%@]+) *:? *([^{;]+?);|([^;}{]*?) *{)|(}\s*)/g,pa=/\/\*[^]*?\*\/|  +/g,Rs=/\n+/g,fe=(t,e)=>{let s="",r="",i="";for(let n in t){let o=t[n];n[0]=="@"?n[1]=="i"?s=n+" "+o+";":r+=n[1]=="f"?fe(o,n):n+"{"+fe(o,n[1]=="k"?"":e)+"}":typeof o=="object"?r+=fe(o,e?e.replace(/([^,])+/g,l=>n.replace(/([^,]*:\S+\([^)]*\))|([^,])+/g,u=>/&/.test(u)?u.replace(/&/g,l):l?l+" "+u:u)):n):o!=null&&(n=/^--/.test(n)?n:n.replace(/[A-Z]/g,"-$&").toLowerCase(),i+=fe.p?fe.p(n,o):n+":"+o+";")}return s+(e&&i?e+"{"+i+"}":i)+r},re={},pr=t=>{if(typeof t=="object"){let e="";for(let s in t)e+=s+pr(t[s]);return e}return t},fa=(t,e,s,r,i)=>{let n=pr(t),o=re[n]||(re[n]=(u=>{let p=0,y=11;for(;p<u.length;)y=101*y+u.charCodeAt(p++)>>>0;return"go"+y})(n));if(!re[o]){let u=n!==t?t:(p=>{let y,h,b=[{}];for(;y=da.exec(p.replace(pa,""));)y[4]?b.shift():y[3]?(h=y[3].replace(Rs," ").trim(),b.unshift(b[0][h]=b[0][h]||{})):b[0][y[1]]=y[2].replace(Rs," ").trim();return b[0]})(t);re[o]=fe(i?{["@keyframes "+o]:u}:u,s?"":"."+o)}let l=s&&re.g?re.g:null;return s&&(re.g=re[o]),((u,p,y,h)=>{h?p.data=p.data.replace(h,u):p.data.indexOf(u)===-1&&(p.data=y?u+p.data:p.data+u)})(re[o],e,r,l),o},ya=(t,e,s)=>t.reduce((r,i,n)=>{let o=e[n];if(o&&o.call){let l=o(s),u=l&&l.props&&l.props.className||/^go/.test(l)&&l;o=u?"."+u:l&&typeof l=="object"?l.props?"":fe(l,""):l===!1?"":l}return r+i+(o??"")},"");function Et(t){let e=this||{},s=t.call?t(e.p):t;return fa(s.unshift?s.raw?ya(s,[].slice.call(arguments,1),e.p):s.reduce((r,i)=>Object.assign(r,i&&i.call?i(e.p):i),{}):s,ua(e.target),e.g,e.o,e.k)}let fr,ts,ss;Et.bind({g:1});let ue=Et.bind({k:1});function ma(t,e,s,r){fe.p=e,fr=t,ts=s,ss=r}function Ne(t,e){let s=this||{};return function(){let r=arguments;function i(n,o){let l=Object.assign({},n),u=l.className||i.className;s.p=Object.assign({theme:ts&&ts()},l),s.o=/ *go\d+/.test(u),l.className=Et.apply(s,r)+(u?" "+u:"");let p=t;return t[0]&&(p=l.as||t,delete l.as),ss&&p[0]&&ss(l),fr(p,l)}return i}}var ga=t=>typeof t=="function",St=(t,e)=>ga(t)?t(e):t,xa=(()=>{let t=0;return()=>(++t).toString()})(),yr=(()=>{let t;return()=>{if(t===void 0&&typeof window<"u"){let e=matchMedia("(prefers-reduced-motion: reduce)");t=!e||e.matches}return t}})(),va=20,ps="default",mr=(t,e)=>{let{toastLimit:s}=t.settings;switch(e.type){case 0:return{...t,toasts:[e.toast,...t.toasts].slice(0,s)};case 1:return{...t,toasts:t.toasts.map(o=>o.id===e.toast.id?{...o,...e.toast}:o)};case 2:let{toast:r}=e;return mr(t,{type:t.toasts.find(o=>o.id===r.id)?1:0,toast:r});case 3:let{toastId:i}=e;return{...t,toasts:t.toasts.map(o=>o.id===i||i===void 0?{...o,dismissed:!0,visible:!1}:o)};case 4:return e.toastId===void 0?{...t,toasts:[]}:{...t,toasts:t.toasts.filter(o=>o.id!==e.toastId)};case 5:return{...t,pausedAt:e.time};case 6:let n=e.time-(t.pausedAt||0);return{...t,pausedAt:void 0,toasts:t.toasts.map(o=>({...o,pauseDuration:o.pauseDuration+n}))}}},jt=[],gr={toasts:[],pausedAt:void 0,settings:{toastLimit:va}},se={},xr=(t,e=ps)=>{se[e]=mr(se[e]||gr,t),jt.forEach(([s,r])=>{s===e&&r(se[e])})},vr=t=>Object.keys(se).forEach(e=>xr(t,e)),ba=t=>Object.keys(se).find(e=>se[e].toasts.some(s=>s.id===t)),Rt=(t=ps)=>e=>{xr(e,t)},ka={blank:4e3,error:4e3,success:2e3,loading:1/0,custom:4e3},wa=(t={},e=ps)=>{let[s,r]=m.useState(se[e]||gr),i=m.useRef(se[e]);m.useEffect(()=>(i.current!==se[e]&&r(se[e]),jt.push([e,r]),()=>{let o=jt.findIndex(([l])=>l===e);o>-1&&jt.splice(o,1)}),[e]);let n=s.toasts.map(o=>{var l,u,p;return{...t,...t[o.type],...o,removeDelay:o.removeDelay||((l=t[o.type])==null?void 0:l.removeDelay)||(t==null?void 0:t.removeDelay),duration:o.duration||((u=t[o.type])==null?void 0:u.duration)||(t==null?void 0:t.duration)||ka[o.type],style:{...t.style,...(p=t[o.type])==null?void 0:p.style,...o.style}}});return{...s,toasts:n}},Ma=(t,e="blank",s)=>({createdAt:Date.now(),visible:!0,dismissed:!1,type:e,ariaProps:{role:"status","aria-live":"polite"},message:t,pauseDuration:0,...s,id:(s==null?void 0:s.id)||xa()}),gt=t=>(e,s)=>{let r=Ma(e,t,s);return Rt(r.toasterId||ba(r.id))({type:2,toast:r}),r.id},F=(t,e)=>gt("blank")(t,e);F.error=gt("error");F.success=gt("success");F.loading=gt("loading");F.custom=gt("custom");F.dismiss=(t,e)=>{let s={type:3,toastId:t};e?Rt(e)(s):vr(s)};F.dismissAll=t=>F.dismiss(void 0,t);F.remove=(t,e)=>{let s={type:4,toastId:t};e?Rt(e)(s):vr(s)};F.removeAll=t=>F.remove(void 0,t);F.promise=(t,e,s)=>{let r=F.loading(e.loading,{...s,...s==null?void 0:s.loading});return typeof t=="function"&&(t=t()),t.then(i=>{let n=e.success?St(e.success,i):void 0;return n?F.success(n,{id:r,...s,...s==null?void 0:s.success}):F.dismiss(r),i}).catch(i=>{let n=e.error?St(e.error,i):void 0;n?F.error(n,{id:r,...s,...s==null?void 0:s.error}):F.dismiss(r)}),t};var Ca=1e3,ja=(t,e="default")=>{let{toasts:s,pausedAt:r}=wa(t,e),i=m.useRef(new Map).current,n=m.useCallback((h,b=Ca)=>{if(i.has(h))return;let g=setTimeout(()=>{i.delete(h),o({type:4,toastId:h})},b);i.set(h,g)},[]);m.useEffect(()=>{if(r)return;let h=Date.now(),b=s.map(g=>{if(g.duration===1/0)return;let N=(g.duration||0)+g.pauseDuration-(h-g.createdAt);if(N<0){g.visible&&F.dismiss(g.id);return}return setTimeout(()=>F.dismiss(g.id,e),N)});return()=>{b.forEach(g=>g&&clearTimeout(g))}},[s,r,e]);let o=m.useCallback(Rt(e),[e]),l=m.useCallback(()=>{o({type:5,time:Date.now()})},[o]),u=m.useCallback((h,b)=>{o({type:1,toast:{id:h,height:b}})},[o]),p=m.useCallback(()=>{r&&o({type:6,time:Date.now()})},[r,o]),y=m.useCallback((h,b)=>{let{reverseOrder:g=!1,gutter:N=8,defaultPosition:w}=b||{},j=s.filter(k=>(k.position||w)===(h.position||w)&&k.height),S=j.findIndex(k=>k.id===h.id),O=j.filter((k,M)=>M<S&&k.visible).length;return j.filter(k=>k.visible).slice(...g?[O+1]:[0,O]).reduce((k,M)=>k+(M.height||0)+N,0)},[s]);return m.useEffect(()=>{s.forEach(h=>{if(h.dismissed)n(h.id,h.removeDelay);else{let b=i.get(h.id);b&&(clearTimeout(b),i.delete(h.id))}})},[s,n]),{toasts:s,handlers:{updateHeight:u,startPause:l,endPause:p,calculateOffset:y}}},Na=ue`
from {
  transform: scale(0) rotate(45deg);
	opacity: 0;
}
to {
 transform: scale(1) rotate(45deg);
  opacity: 1;
}`,Sa=ue`
from {
  transform: scale(0);
  opacity: 0;
}
to {
  transform: scale(1);
  opacity: 1;
}`,_a=ue`
from {
  transform: scale(0) rotate(90deg);
	opacity: 0;
}
to {
  transform: scale(1) rotate(90deg);
	opacity: 1;
}`,Ea=Ne("div")`
  width: 20px;
  opacity: 0;
  height: 20px;
  border-radius: 10px;
  background: ${t=>t.primary||"#ff4b4b"};
  position: relative;
  transform: rotate(45deg);

  animation: ${Na} 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275)
    forwards;
  animation-delay: 100ms;

  &:after,
  &:before {
    content: '';
    animation: ${Sa} 0.15s ease-out forwards;
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
    animation: ${_a} 0.15s ease-out forwards;
    animation-delay: 180ms;
    transform: rotate(90deg);
  }
`,Ra=ue`
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
`,Oa=Ne("div")`
  width: 12px;
  height: 12px;
  box-sizing: border-box;
  border: 2px solid;
  border-radius: 100%;
  border-color: ${t=>t.secondary||"#e0e0e0"};
  border-right-color: ${t=>t.primary||"#616161"};
  animation: ${Ra} 1s linear infinite;
`,Pa=ue`
from {
  transform: scale(0) rotate(45deg);
	opacity: 0;
}
to {
  transform: scale(1) rotate(45deg);
	opacity: 1;
}`,$a=ue`
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
}`,Ta=Ne("div")`
  width: 20px;
  opacity: 0;
  height: 20px;
  border-radius: 10px;
  background: ${t=>t.primary||"#61d345"};
  position: relative;
  transform: rotate(45deg);

  animation: ${Pa} 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275)
    forwards;
  animation-delay: 100ms;
  &:after {
    content: '';
    box-sizing: border-box;
    animation: ${$a} 0.2s ease-out forwards;
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
`,Aa=Ne("div")`
  position: absolute;
`,Ia=Ne("div")`
  position: relative;
  display: flex;
  justify-content: center;
  align-items: center;
  min-width: 20px;
  min-height: 20px;
`,Fa=ue`
from {
  transform: scale(0.6);
  opacity: 0.4;
}
to {
  transform: scale(1);
  opacity: 1;
}`,Da=Ne("div")`
  position: relative;
  transform: scale(0.6);
  opacity: 0.4;
  min-width: 20px;
  animation: ${Fa} 0.3s 0.12s cubic-bezier(0.175, 0.885, 0.32, 1.275)
    forwards;
`,Qa=({toast:t})=>{let{icon:e,type:s,iconTheme:r}=t;return e!==void 0?typeof e=="string"?m.createElement(Da,null,e):e:s==="blank"?null:m.createElement(Ia,null,m.createElement(Oa,{...r}),s!=="loading"&&m.createElement(Aa,null,s==="error"?m.createElement(Ea,{...r}):m.createElement(Ta,{...r})))},qa=t=>`
0% {transform: translate3d(0,${t*-200}%,0) scale(.6); opacity:.5;}
100% {transform: translate3d(0,0,0) scale(1); opacity:1;}
`,La=t=>`
0% {transform: translate3d(0,0,-1px) scale(1); opacity:1;}
100% {transform: translate3d(0,${t*-150}%,-1px) scale(.6); opacity:0;}
`,za="0%{opacity:0;} 100%{opacity:1;}",Ua="0%{opacity:1;} 100%{opacity:0;}",Ha=Ne("div")`
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
`,Ka=Ne("div")`
  display: flex;
  justify-content: center;
  margin: 4px 10px;
  color: inherit;
  flex: 1 1 auto;
  white-space: pre-line;
`,Va=(t,e)=>{let s=t.includes("top")?1:-1,[r,i]=yr()?[za,Ua]:[qa(s),La(s)];return{animation:e?`${ue(r)} 0.35s cubic-bezier(.21,1.02,.73,1) forwards`:`${ue(i)} 0.4s forwards cubic-bezier(.06,.71,.55,1)`}},Ba=m.memo(({toast:t,position:e,style:s,children:r})=>{let i=t.height?Va(t.position||e||"top-center",t.visible):{opacity:0},n=m.createElement(Qa,{toast:t}),o=m.createElement(Ka,{...t.ariaProps},St(t.message,t));return m.createElement(Ha,{className:t.className,style:{...i,...s,...t.style}},typeof r=="function"?r({icon:n,message:o}):m.createElement(m.Fragment,null,n,o))});ma(m.createElement);var Ga=({id:t,className:e,style:s,onHeightUpdate:r,children:i})=>{let n=m.useCallback(o=>{if(o){let l=()=>{let u=o.getBoundingClientRect().height;r(t,u)};l(),new MutationObserver(l).observe(o,{subtree:!0,childList:!0,characterData:!0})}},[t,r]);return m.createElement("div",{ref:n,className:e,style:s},i)},Ja=(t,e)=>{let s=t.includes("top"),r=s?{top:0}:{bottom:0},i=t.includes("center")?{justifyContent:"center"}:t.includes("right")?{justifyContent:"flex-end"}:{};return{left:0,right:0,display:"flex",position:"absolute",transition:yr()?void 0:"all 230ms cubic-bezier(.21,1.02,.73,1)",transform:`translateY(${e*(s?1:-1)}px)`,...r,...i}},Za=Et`
  z-index: 9999;
  > * {
    pointer-events: auto;
  }
`,Mt=16,to=({reverseOrder:t,position:e="top-center",toastOptions:s,gutter:r,children:i,toasterId:n,containerStyle:o,containerClassName:l})=>{let{toasts:u,handlers:p}=ja(s,n);return m.createElement("div",{"data-rht-toaster":n||"",style:{position:"fixed",zIndex:9999,top:Mt,left:Mt,right:Mt,bottom:Mt,pointerEvents:"none",...o},className:l,onMouseEnter:p.startPause,onMouseLeave:p.endPause},u.map(y=>{let h=y.position||e,b=p.calculateOffset(y,{reverseOrder:t,gutter:r,defaultPosition:e}),g=Ja(h,b);return m.createElement(Ga,{id:y.id,key:y.id,onHeightUpdate:p.updateHeight,className:y.visible?Za:"",style:g},y.type==="custom"?St(y.message,y):i?i(y):m.createElement(Ba,{toast:y,position:h}))}))},Y=F;/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Wa=t=>t.replace(/([a-z0-9])([A-Z])/g,"$1-$2").toLowerCase(),Ya=t=>t.replace(/^([A-Z])|[\s-_]+(\w)/g,(e,s,r)=>r?r.toUpperCase():s.toLowerCase()),Os=t=>{const e=Ya(t);return e.charAt(0).toUpperCase()+e.slice(1)},br=(...t)=>t.filter((e,s,r)=>!!e&&e.trim()!==""&&r.indexOf(e)===s).join(" ").trim(),Xa=t=>{for(const e in t)if(e.startsWith("aria-")||e==="role"||e==="title")return!0};/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */var ei={xmlns:"http://www.w3.org/2000/svg",width:24,height:24,viewBox:"0 0 24 24",fill:"none",stroke:"currentColor",strokeWidth:2,strokeLinecap:"round",strokeLinejoin:"round"};/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ti=m.forwardRef(({color:t="currentColor",size:e=24,strokeWidth:s=2,absoluteStrokeWidth:r,className:i="",children:n,iconNode:o,...l},u)=>m.createElement("svg",{ref:u,...ei,width:e,height:e,stroke:t,strokeWidth:r?Number(s)*24/Number(e):s,className:br("lucide",i),...!n&&!Xa(l)&&{"aria-hidden":"true"},...l},[...o.map(([p,y])=>m.createElement(p,y)),...Array.isArray(n)?n:[n]]));/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const f=(t,e)=>{const s=m.forwardRef(({className:r,...i},n)=>m.createElement(ti,{ref:n,iconNode:e,className:br(`lucide-${Wa(Os(t))}`,`lucide-${t}`,r),...i}));return s.displayName=Os(t),s};/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const si=[["path",{d:"M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2",key:"169zse"}]],so=f("activity",si);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ri=[["rect",{width:"20",height:"5",x:"2",y:"3",rx:"1",key:"1wp1u1"}],["path",{d:"M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8",key:"1s80jp"}],["path",{d:"M10 12h4",key:"a56b0p"}]],ro=f("archive",ri);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ai=[["path",{d:"M5 12h14",key:"1ays0h"}],["path",{d:"m12 5 7 7-7 7",key:"xquz4c"}]],ao=f("arrow-right",ai);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ii=[["path",{d:"M10.268 21a2 2 0 0 0 3.464 0",key:"vwvbt9"}],["path",{d:"M3.262 15.326A1 1 0 0 0 4 17h16a1 1 0 0 0 .74-1.673C19.41 13.956 18 12.499 18 8A6 6 0 0 0 6 8c0 4.499-1.411 5.956-2.738 7.326",key:"11g9vi"}]],io=f("bell",ii);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ni=[["path",{d:"M12 7v14",key:"1akyts"}],["path",{d:"M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z",key:"ruj8y"}]],no=f("book-open",ni);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const oi=[["path",{d:"M12 8V4H8",key:"hb8ula"}],["rect",{width:"16",height:"12",x:"4",y:"8",rx:"2",key:"enze0r"}],["path",{d:"M2 14h2",key:"vft8re"}],["path",{d:"M20 14h2",key:"4cs60a"}],["path",{d:"M15 13v2",key:"1xurst"}],["path",{d:"M9 13v2",key:"rq6x2g"}]],oo=f("bot",oi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ci=[["path",{d:"M2.97 12.92A2 2 0 0 0 2 14.63v3.24a2 2 0 0 0 .97 1.71l3 1.8a2 2 0 0 0 2.06 0L12 19v-5.5l-5-3-4.03 2.42Z",key:"lc1i9w"}],["path",{d:"m7 16.5-4.74-2.85",key:"1o9zyk"}],["path",{d:"m7 16.5 5-3",key:"va8pkn"}],["path",{d:"M7 16.5v5.17",key:"jnp8gn"}],["path",{d:"M12 13.5V19l3.97 2.38a2 2 0 0 0 2.06 0l3-1.8a2 2 0 0 0 .97-1.71v-3.24a2 2 0 0 0-.97-1.71L17 10.5l-5 3Z",key:"8zsnat"}],["path",{d:"m17 16.5-5-3",key:"8arw3v"}],["path",{d:"m17 16.5 4.74-2.85",key:"8rfmw"}],["path",{d:"M17 16.5v5.17",key:"k6z78m"}],["path",{d:"M7.97 4.42A2 2 0 0 0 7 6.13v4.37l5 3 5-3V6.13a2 2 0 0 0-.97-1.71l-3-1.8a2 2 0 0 0-2.06 0l-3 1.8Z",key:"1xygjf"}],["path",{d:"M12 8 7.26 5.15",key:"1vbdud"}],["path",{d:"m12 8 4.74-2.85",key:"3rx089"}],["path",{d:"M12 13.5V8",key:"1io7kd"}]],co=f("boxes",ci);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const li=[["path",{d:"M12 20v-9",key:"1qisl0"}],["path",{d:"M14 7a4 4 0 0 1 4 4v3a6 6 0 0 1-12 0v-3a4 4 0 0 1 4-4z",key:"uouzyp"}],["path",{d:"M14.12 3.88 16 2",key:"qol33r"}],["path",{d:"M21 21a4 4 0 0 0-3.81-4",key:"1b0z45"}],["path",{d:"M21 5a4 4 0 0 1-3.55 3.97",key:"5cxbf6"}],["path",{d:"M22 13h-4",key:"1jl80f"}],["path",{d:"M3 21a4 4 0 0 1 3.81-4",key:"1fjd4g"}],["path",{d:"M3 5a4 4 0 0 0 3.55 3.97",key:"1d7oge"}],["path",{d:"M6 13H2",key:"82j7cp"}],["path",{d:"m8 2 1.88 1.88",key:"fmnt4t"}],["path",{d:"M9 7.13V6a3 3 0 1 1 6 0v1.13",key:"1vgav8"}]],lo=f("bug",li);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const hi=[["path",{d:"M16 14v2.2l1.6 1",key:"fo4ql5"}],["path",{d:"M16 2v4",key:"4m81vk"}],["path",{d:"M21 7.5V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h3.5",key:"1osxxc"}],["path",{d:"M3 10h5",key:"r794hk"}],["path",{d:"M8 2v4",key:"1cmpym"}],["circle",{cx:"16",cy:"16",r:"6",key:"qoo3c4"}]],ho=f("calendar-clock",hi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ui=[["path",{d:"M8 2v4",key:"1cmpym"}],["path",{d:"M16 2v4",key:"4m81vk"}],["rect",{width:"18",height:"18",x:"3",y:"4",rx:"2",key:"1hopcy"}],["path",{d:"M3 10h18",key:"8toen8"}]],uo=f("calendar",ui);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const di=[["path",{d:"M3 3v16a2 2 0 0 0 2 2h16",key:"c24i48"}],["path",{d:"M18 17V9",key:"2bz60n"}],["path",{d:"M13 17V5",key:"1frdt8"}],["path",{d:"M8 17v-3",key:"17ska0"}]],po=f("chart-column",di);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const pi=[["path",{d:"M5 21v-6",key:"1hz6c0"}],["path",{d:"M12 21V3",key:"1lcnhd"}],["path",{d:"M19 21V9",key:"unv183"}]],fo=f("chart-no-axes-column",pi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const fi=[["path",{d:"M20 6 9 17l-5-5",key:"1gmf2c"}]],yi=f("check",fi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const mi=[["path",{d:"m6 9 6 6 6-6",key:"qrunsl"}]],gi=f("chevron-down",mi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const xi=[["path",{d:"m15 18-6-6 6-6",key:"1wnfg3"}]],yo=f("chevron-left",xi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const vi=[["path",{d:"m9 18 6-6-6-6",key:"mthhwq"}]],mo=f("chevron-right",vi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const bi=[["path",{d:"m18 15-6-6-6 6",key:"153udz"}]],ki=f("chevron-up",bi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const wi=[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["line",{x1:"12",x2:"12",y1:"8",y2:"12",key:"1pkeuh"}],["line",{x1:"12",x2:"12.01",y1:"16",y2:"16",key:"4dfq90"}]],go=f("circle-alert",wi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Mi=[["path",{d:"M21.801 10A10 10 0 1 1 17 3.335",key:"yps3ct"}],["path",{d:"m9 11 3 3L22 4",key:"1pflzl"}]],Ci=f("circle-check-big",Mi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ji=[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3",key:"1u773s"}],["path",{d:"M12 17h.01",key:"p32p05"}]],xo=f("circle-question-mark",ji);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ni=[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"m15 9-6 6",key:"1uzhvr"}],["path",{d:"m9 9 6 6",key:"z0biqf"}]],vo=f("circle-x",Ni);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Si=[["rect",{width:"8",height:"4",x:"8",y:"2",rx:"1",ry:"1",key:"tgr4d6"}],["path",{d:"M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2",key:"116196"}],["path",{d:"M12 11h4",key:"1jrz19"}],["path",{d:"M12 16h4",key:"n85exb"}],["path",{d:"M8 11h.01",key:"1dfujw"}],["path",{d:"M8 16h.01",key:"18s6g9"}]],bo=f("clipboard-list",Si);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const _i=[["rect",{width:"8",height:"4",x:"8",y:"2",rx:"1",ry:"1",key:"tgr4d6"}],["path",{d:"M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2",key:"116196"}]],ko=f("clipboard",_i);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ei=[["path",{d:"M12 6v6l4 2",key:"mmk7yg"}],["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}]],Ri=f("clock",Ei);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Oi=[["path",{d:"M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z",key:"p7xjir"}]],wo=f("cloud",Oi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Pi=[["path",{d:"m16 18 6-6-6-6",key:"eg8j8"}],["path",{d:"m8 6-6 6 6 6",key:"ppft3o"}]],Mo=f("code",Pi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const $i=[["rect",{width:"14",height:"14",x:"8",y:"8",rx:"2",ry:"2",key:"17jyea"}],["path",{d:"M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2",key:"zix9uf"}]],Ti=f("copy",$i);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ai=[["path",{d:"M12 20v2",key:"1lh1kg"}],["path",{d:"M12 2v2",key:"tus03m"}],["path",{d:"M17 20v2",key:"1rnc9c"}],["path",{d:"M17 2v2",key:"11trls"}],["path",{d:"M2 12h2",key:"1t8f8n"}],["path",{d:"M2 17h2",key:"7oei6x"}],["path",{d:"M2 7h2",key:"asdhe0"}],["path",{d:"M20 12h2",key:"1q8mjw"}],["path",{d:"M20 17h2",key:"1fpfkl"}],["path",{d:"M20 7h2",key:"1o8tra"}],["path",{d:"M7 20v2",key:"4gnj0m"}],["path",{d:"M7 2v2",key:"1i4yhu"}],["rect",{x:"4",y:"4",width:"16",height:"16",rx:"2",key:"1vbyd7"}],["rect",{x:"8",y:"8",width:"8",height:"8",rx:"1",key:"z9xiuo"}]],Co=f("cpu",Ai);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ii=[["rect",{width:"20",height:"14",x:"2",y:"5",rx:"2",key:"ynyp8z"}],["line",{x1:"2",x2:"22",y1:"10",y2:"10",key:"1b3vmo"}]],jo=f("credit-card",Ii);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Fi=[["ellipse",{cx:"12",cy:"5",rx:"9",ry:"3",key:"msslwz"}],["path",{d:"M3 5V19A9 3 0 0 0 21 19V5",key:"1wlel7"}],["path",{d:"M3 12A9 3 0 0 0 21 12",key:"mv7ke4"}]],kr=f("database",Fi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Di=[["path",{d:"M12 15V3",key:"m9g1x1"}],["path",{d:"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4",key:"ih7n3h"}],["path",{d:"m7 10 5 5 5-5",key:"brsn70"}]],Ps=f("download",Di);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Qi=[["path",{d:"M15 3h6v6",key:"1q9fwt"}],["path",{d:"M10 14 21 3",key:"gplh6r"}],["path",{d:"M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",key:"a6xqqp"}]],No=f("external-link",Qi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const qi=[["path",{d:"M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49",key:"ct8e1f"}],["path",{d:"M14.084 14.158a3 3 0 0 1-4.242-4.242",key:"151rxh"}],["path",{d:"M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143",key:"13bj9a"}],["path",{d:"m2 2 20 20",key:"1ooewy"}]],So=f("eye-off",qi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Li=[["path",{d:"M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0",key:"1nclc0"}],["circle",{cx:"12",cy:"12",r:"3",key:"1v7zrd"}]],_o=f("eye",Li);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const zi=[["path",{d:"M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z",key:"1oefj6"}],["path",{d:"M14 2v5a1 1 0 0 0 1 1h5",key:"wfsgrz"}],["path",{d:"M10 12.5 8 15l2 2.5",key:"1tg20x"}],["path",{d:"m14 12.5 2 2.5-2 2.5",key:"yinavb"}]],Eo=f("file-code",zi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ui=[["path",{d:"M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z",key:"1oefj6"}],["path",{d:"M14 2v5a1 1 0 0 0 1 1h5",key:"wfsgrz"}],["path",{d:"M10 9H8",key:"b1mrlr"}],["path",{d:"M16 13H8",key:"t4e002"}],["path",{d:"M16 17H8",key:"z1uh3a"}]],Ro=f("file-text",Ui);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Hi=[["path",{d:"M12 3q1 4 4 6.5t3 5.5a1 1 0 0 1-14 0 5 5 0 0 1 1-3 1 1 0 0 0 5 0c0-2-1.5-3-1.5-5q0-2 2.5-4",key:"1slcih"}]],Oo=f("flame",Hi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ki=[["path",{d:"M14 2v6a2 2 0 0 0 .245.96l5.51 10.08A2 2 0 0 1 18 22H6a2 2 0 0 1-1.755-2.96l5.51-10.08A2 2 0 0 0 10 8V2",key:"18mbvz"}],["path",{d:"M6.453 15h11.094",key:"3shlmq"}],["path",{d:"M8.5 2h7",key:"csnxdl"}]],Po=f("flask-conical",Ki);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Vi=[["path",{d:"M10 20a1 1 0 0 0 .553.895l2 1A1 1 0 0 0 14 21v-7a2 2 0 0 1 .517-1.341L21.74 4.67A1 1 0 0 0 21 3H3a1 1 0 0 0-.742 1.67l7.225 7.989A2 2 0 0 1 10 14z",key:"sc7q7i"}]],$s=f("funnel",Vi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Bi=[["circle",{cx:"18",cy:"18",r:"3",key:"1xkwt0"}],["circle",{cx:"6",cy:"6",r:"3",key:"1lh9wr"}],["path",{d:"M6 21V9a9 9 0 0 0 9 9",key:"7kw0sc"}]],wr=f("git-merge",Bi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Gi=[["path",{d:"M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4",key:"tonef"}],["path",{d:"M9 18c-4.51 2-5-2-7-2",key:"9comsn"}]],$o=f("github",Gi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ji=[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20",key:"13o1zl"}],["path",{d:"M2 12h20",key:"9i4pu4"}]],To=f("globe",Ji);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Zi=[["line",{x1:"22",x2:"2",y1:"12",y2:"12",key:"1y58io"}],["path",{d:"M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z",key:"oot6mr"}],["line",{x1:"6",x2:"6.01",y1:"16",y2:"16",key:"sgf278"}],["line",{x1:"10",x2:"10.01",y1:"16",y2:"16",key:"1l4acy"}]],Ao=f("hard-drive",Zi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Wi=[["polyline",{points:"22 12 16 12 14 15 10 15 8 12 2 12",key:"o97t9d"}],["path",{d:"M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z",key:"oot6mr"}]],Io=f("inbox",Wi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Yi=[["circle",{cx:"12",cy:"12",r:"10",key:"1mglay"}],["path",{d:"M12 16v-4",key:"1dtifu"}],["path",{d:"M12 8h.01",key:"e9boi3"}]],Fo=f("info",Yi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Xi=[["path",{d:"M10 8h.01",key:"1r9ogq"}],["path",{d:"M12 12h.01",key:"1mp3jc"}],["path",{d:"M14 8h.01",key:"1primd"}],["path",{d:"M16 12h.01",key:"1l6xoz"}],["path",{d:"M18 8h.01",key:"emo2bl"}],["path",{d:"M6 8h.01",key:"x9i8wu"}],["path",{d:"M7 16h10",key:"wp8him"}],["path",{d:"M8 12h.01",key:"czm47f"}],["rect",{width:"20",height:"16",x:"2",y:"4",rx:"2",key:"18n3k1"}]],Do=f("keyboard",Xi);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const en=[["rect",{width:"7",height:"9",x:"3",y:"3",rx:"1",key:"10lvy0"}],["rect",{width:"7",height:"5",x:"14",y:"3",rx:"1",key:"16une8"}],["rect",{width:"7",height:"9",x:"14",y:"12",rx:"1",key:"1hutg5"}],["rect",{width:"7",height:"5",x:"3",y:"16",rx:"1",key:"ldoo1y"}]],Qo=f("layout-dashboard",en);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const tn=[["path",{d:"M3 5h.01",key:"18ugdj"}],["path",{d:"M3 12h.01",key:"nlz23k"}],["path",{d:"M3 19h.01",key:"noohij"}],["path",{d:"M8 5h13",key:"1pao27"}],["path",{d:"M8 12h13",key:"1za7za"}],["path",{d:"M8 19h13",key:"m83p4d"}]],qo=f("list",tn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const sn=[["path",{d:"M21 12a9 9 0 1 1-6.219-8.56",key:"13zald"}]],Lo=f("loader-circle",sn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const rn=[["rect",{width:"18",height:"11",x:"3",y:"11",rx:"2",ry:"2",key:"1w4ew1"}],["path",{d:"M7 11V7a5 5 0 0 1 10 0v4",key:"fwvmzm"}]],zo=f("lock",rn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const an=[["path",{d:"m22 7-8.991 5.727a2 2 0 0 1-2.009 0L2 7",key:"132q7q"}],["rect",{x:"2",y:"4",width:"20",height:"16",rx:"2",key:"izxlao"}]],Uo=f("mail",an);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const nn=[["path",{d:"M22 17a2 2 0 0 1-2 2H6.828a2 2 0 0 0-1.414.586l-2.202 2.202A.71.71 0 0 1 2 21.286V5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2z",key:"18887p"}]],Ho=f("message-square",nn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const on=[["rect",{width:"20",height:"14",x:"2",y:"3",rx:"2",key:"48i651"}],["line",{x1:"8",x2:"16",y1:"21",y2:"21",key:"1svkeh"}],["line",{x1:"12",x2:"12",y1:"17",y2:"21",key:"vw1qmm"}]],Ko=f("monitor",on);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const cn=[["path",{d:"M15 18h-5",key:"95g1m2"}],["path",{d:"M18 14h-8",key:"sponae"}],["path",{d:"M4 22h16a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2H8a2 2 0 0 0-2 2v16a2 2 0 0 1-4 0v-9a2 2 0 0 1 2-2h2",key:"39pd36"}],["rect",{width:"8",height:"4",x:"10",y:"6",rx:"1",key:"aywv1n"}]],Vo=f("newspaper",cn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const ln=[["path",{d:"M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z",key:"1a0edw"}],["path",{d:"M12 22V12",key:"d0xqtd"}],["polyline",{points:"3.29 7 12 12 20.71 7",key:"ousv84"}],["path",{d:"m7.5 4.27 9 5.15",key:"1c824w"}]],Bo=f("package",ln);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const hn=[["rect",{x:"14",y:"3",width:"5",height:"18",rx:"1",key:"kaeet6"}],["rect",{x:"5",y:"3",width:"5",height:"18",rx:"1",key:"1wsw3u"}]],Go=f("pause",hn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const un=[["path",{d:"M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z",key:"1a8usu"}],["path",{d:"m15 5 4 4",key:"1mk7zo"}]],Jo=f("pencil",un);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const dn=[["path",{d:"M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z",key:"10ikf1"}]],Zo=f("play",dn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const pn=[["path",{d:"M12 22v-5",key:"1ega77"}],["path",{d:"M15 8V2",key:"18g5xt"}],["path",{d:"M17 8a1 1 0 0 1 1 1v4a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V9a1 1 0 0 1 1-1z",key:"1xoxul"}],["path",{d:"M9 8V2",key:"14iosj"}]],Wo=f("plug",pn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const fn=[["path",{d:"M5 12h14",key:"1ays0h"}],["path",{d:"M12 5v14",key:"s699le"}]],Yo=f("plus",fn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const yn=[["path",{d:"M16.247 7.761a6 6 0 0 1 0 8.478",key:"1fwjs5"}],["path",{d:"M19.075 4.933a10 10 0 0 1 0 14.134",key:"ehdyv1"}],["path",{d:"M4.925 19.067a10 10 0 0 1 0-14.134",key:"1q22gi"}],["path",{d:"M7.753 16.239a6 6 0 0 1 0-8.478",key:"r2q7qm"}],["circle",{cx:"12",cy:"12",r:"2",key:"1c9p78"}]],Mr=f("radio",yn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const mn=[["path",{d:"M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8",key:"v9h5vc"}],["path",{d:"M21 3v5h-5",key:"1q7to0"}],["path",{d:"M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16",key:"3uifl3"}],["path",{d:"M8 16H3v5",key:"1cv678"}]],Xo=f("refresh-cw",mn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const gn=[["path",{d:"m21 21-4.34-4.34",key:"14j7rj"}],["circle",{cx:"11",cy:"11",r:"8",key:"4ej97u"}]],Ts=f("search",gn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const xn=[["path",{d:"M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z",key:"1ffxy3"}],["path",{d:"m21.854 2.147-10.94 10.939",key:"12cjpa"}]],ec=f("send",xn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const vn=[["rect",{width:"20",height:"8",x:"2",y:"2",rx:"2",ry:"2",key:"ngkwjq"}],["rect",{width:"20",height:"8",x:"2",y:"14",rx:"2",ry:"2",key:"iecqi9"}],["line",{x1:"6",x2:"6.01",y1:"6",y2:"6",key:"16zg32"}],["line",{x1:"6",x2:"6.01",y1:"18",y2:"18",key:"nzw8ys"}]],tc=f("server",vn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const bn=[["path",{d:"M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",key:"oel41y"}]],sc=f("shield",bn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const kn=[["path",{d:"m12.5 17-.5-1-.5 1h1z",key:"3me087"}],["path",{d:"M15 22a1 1 0 0 0 1-1v-1a2 2 0 0 0 1.56-3.25 8 8 0 1 0-11.12 0A2 2 0 0 0 8 20v1a1 1 0 0 0 1 1z",key:"1o5pge"}],["circle",{cx:"15",cy:"12",r:"1",key:"1tmaij"}],["circle",{cx:"9",cy:"12",r:"1",key:"1vctgf"}]],rc=f("skull",kn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const wn=[["path",{d:"M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z",key:"1s2grr"}],["path",{d:"M20 2v4",key:"1rf3ol"}],["path",{d:"M22 4h-4",key:"gwowj6"}],["circle",{cx:"4",cy:"20",r:"2",key:"6kqj1y"}]],ac=f("sparkles",wn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Mn=[["path",{d:"M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z",key:"r04s7s"}]],ic=f("star",Mn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Cn=[["circle",{cx:"9",cy:"12",r:"3",key:"u3jwor"}],["rect",{width:"20",height:"14",x:"2",y:"5",rx:"7",key:"g7kal2"}]],nc=f("toggle-left",Cn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const jn=[["circle",{cx:"15",cy:"12",r:"3",key:"1afu0r"}],["rect",{width:"20",height:"14",x:"2",y:"5",rx:"7",key:"g7kal2"}]],oc=f("toggle-right",jn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Nn=[["path",{d:"M10 11v6",key:"nco0om"}],["path",{d:"M14 11v6",key:"outv1u"}],["path",{d:"M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6",key:"miytrc"}],["path",{d:"M3 6h18",key:"d0wm0j"}],["path",{d:"M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",key:"e791ji"}]],cc=f("trash-2",Nn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Sn=[["path",{d:"M16 7h6v6",key:"box55l"}],["path",{d:"m22 7-8.5 8.5-5-5L2 17",key:"1t1m79"}]],lc=f("trending-up",Sn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const _n=[["path",{d:"m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3",key:"wmoenq"}],["path",{d:"M12 9v4",key:"juzpu7"}],["path",{d:"M12 17h.01",key:"p32p05"}]],Cr=f("triangle-alert",_n);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const En=[["path",{d:"M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2",key:"975kel"}],["circle",{cx:"12",cy:"7",r:"4",key:"17ys0d"}]],hc=f("user",En);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Rn=[["path",{d:"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2",key:"1yyitq"}],["path",{d:"M16 3.128a4 4 0 0 1 0 7.744",key:"16gr8j"}],["path",{d:"M22 21v-2a4 4 0 0 0-3-3.87",key:"kshegd"}],["circle",{cx:"9",cy:"7",r:"4",key:"nufk8"}]],uc=f("users",Rn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const On=[["path",{d:"m21.64 3.64-1.28-1.28a1.21 1.21 0 0 0-1.72 0L2.36 18.64a1.21 1.21 0 0 0 0 1.72l1.28 1.28a1.2 1.2 0 0 0 1.72 0L21.64 5.36a1.2 1.2 0 0 0 0-1.72",key:"ul74o6"}],["path",{d:"m14 7 3 3",key:"1r5n42"}],["path",{d:"M5 6v4",key:"ilb8ba"}],["path",{d:"M19 14v4",key:"blhpug"}],["path",{d:"M10 2v2",key:"7u0qdc"}],["path",{d:"M7 8H3",key:"zfb6yr"}],["path",{d:"M21 16h-4",key:"1cnmox"}],["path",{d:"M11 3H9",key:"1obp7u"}]],dc=f("wand-sparkles",On);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Pn=[["path",{d:"M18 6 6 18",key:"1bl5f8"}],["path",{d:"m6 6 12 12",key:"d8bk6v"}]],pc=f("x",Pn);/**
 * @license lucide-react v0.562.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const $n=[["path",{d:"M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",key:"1xq2db"}]],fc=f("zap",$n),Tn="/api/v1";function _t(){const t=document.querySelector('meta[name="spa-token"]');return(t==null?void 0:t.getAttribute("content"))??null}const le=Rr.create({baseURL:Tn,headers:{"Content-Type":"application/json"},timeout:3e4});le.interceptors.request.use(t=>{const e=_t();return e&&(t.headers["X-SPA-Token"]=e),t});async function jr(t=3){for(let e=0;e<t;e++)try{const s=await fetch("/internal/spa-token",{cache:"no-store"});if(!s.ok){if(e<t-1){await new Promise(n=>setTimeout(n,200*Math.pow(2,e)));continue}return!1}const r=await s.text();if(!(r!=null&&r.trim())){if(e<t-1){await new Promise(n=>setTimeout(n,200*Math.pow(2,e)));continue}return!1}let i=document.querySelector('meta[name="spa-token"]');return i||(i=document.createElement("meta"),i.setAttribute("name","spa-token"),document.head.appendChild(i)),i.setAttribute("content",r.trim()),Nr(),!0}catch{if(e<t-1){await new Promise(s=>setTimeout(s,200*Math.pow(2,e)));continue}return!1}return!1}let $t=null;const An=5400*1e3;function Nr(){$t&&clearTimeout($t),$t=setTimeout(async()=>{await jr()},An)}_t()&&Nr();const st=new Map,As=2e3,In=50;function Qe(t){const e=Date.now();if(st.size>In)for(const[r,i]of st)e-i>As*5&&st.delete(r);const s=st.get(t);return!s||e-s>As?(st.set(t,e),!0):!1}function Fn(t){return["/insights","/$deadletterqueue","/%24deadletterqueue"].some(s=>t.includes(s))}le.interceptors.response.use(t=>t,async t=>{var r,i;if((r=t.config)!=null&&r._silent)return Promise.reject(t);if(!t.response)return Qe("network-error")&&Y.error("Cannot reach the API. If running on a remote server, ensure port 5153 is accessible.",{duration:5e3}),Promise.reject(t);const e=t.response.status,s=((i=t.config)==null?void 0:i.url)||"unknown";switch(e){case 401:{const n=t.config,o=(n==null?void 0:n._spaRetryCount)??0;if(o<2&&_t()!==null&&n&&(n._spaRetryCount=o+1,await jr()))return n.headers=n.headers??{},n.headers["X-SPA-Token"]=_t(),le(n);const l=`${e}-${s}`;Qe(l)&&Y.error("Session expired. Please refresh the page to continue.",{duration:5e3});break}case 403:{const n=`${e}-${s}`;Qe(n)&&Y.error("Access denied. Verify your connection string has the required permissions.",{duration:5e3});break}case 404:{if(!Fn(s)){const n=s.match(/\/messages\/[a-f0-9-]+/i),o=`404-${s}`;Qe(o)&&Y.error(n?"Message not found — it may have been consumed, expired, or already replayed.":"Resource not found.",{duration:4e3})}break}case 422:{const n=t.response.data.errors;if(n){const o=`${e}-validation`;Qe(o)&&Object.values(n).flat().forEach(l=>Y.error(l,{duration:5e3}))}break}case 500:case 502:case 503:{const n=`${e}-server`;Qe(n)&&Y.error("Server error. Try refreshing or restart the API server.",{duration:5e3});break}}return Promise.reject(t)});const fs={list:async()=>(await le.get("/namespaces")).data,create:async t=>(await le.post("/namespaces",t)).data,get:async t=>(await le.get(`/namespaces/${t}`)).data,delete:async t=>{await le.delete(`/namespaces/${t}`)},testConnection:async t=>(await le.post(`/namespaces/${t}/test-connection`)).data};function Dn(){return la({queryKey:["namespaces"],queryFn:fs.list})}function yc(){const t=mt();return ds({mutationFn:e=>fs.create(e),onSuccess:()=>{t.invalidateQueries({queryKey:["namespaces"]}),Y.success("Namespace connected successfully")},onError:e=>{var r,i,n,o;const s=((i=(r=e==null?void 0:e.response)==null?void 0:r.data)==null?void 0:i.detail)||((o=(n=e==null?void 0:e.response)==null?void 0:n.data)==null?void 0:o.message)||(e==null?void 0:e.message)||"Failed to connect namespace. Verify the connection string format and permissions.";Y.error(s,{duration:6e3})}})}function mc(){const t=mt();return ds({mutationFn:e=>fs.delete(e),onSuccess:()=>{t.invalidateQueries({queryKey:["namespaces"]}),Y.success("Namespace deleted")},onError:()=>{Y.error("Failed to delete namespace. The namespace may still be in use.",{duration:5e3})}})}const Qn={searchTimeline:async(t,e)=>{const s={correlationId:t};return e&&(s.namespaceId=e),(await le.get("/correlation/timeline",{params:s})).data}};function qn(){return ds({mutationFn:({correlationId:t,namespaceId:e})=>Qn.searchTimeline(t,e),onError:t=>{var s,r;const e=((r=(s=t==null?void 0:t.response)==null?void 0:s.data)==null?void 0:r.detail)||(t==null?void 0:t.message)||"Correlation search failed";Y.error(e,{duration:5e3})}})}async function Ln(t){try{if(navigator.clipboard&&window.isSecureContext)return await navigator.clipboard.writeText(t),!0;const e=document.createElement("textarea");e.value=t,e.style.position="fixed",e.style.opacity="0",e.style.pointerEvents="none",document.body.appendChild(e),e.focus(),e.select();const s=document.execCommand("copy");return document.body.removeChild(e),s}catch{return!1}}function Sr({text:t,label:e,className:s="",iconSize:r="w-3.5 h-3.5"}){const[i,n]=m.useState(!1),o=m.useCallback(async l=>{l.stopPropagation(),await Ln(t)&&(n(!0),setTimeout(()=>n(!1),2e3))},[t]);return c.jsxs("button",{type:"button",onClick:o,title:e?`Copy ${e}`:"Copy to clipboard","aria-label":e?`Copy ${e}`:"Copy to clipboard",className:`inline-flex items-center gap-1 p-1 rounded transition-colors ${i?"text-green-600 bg-green-50":"text-gray-400 hover:text-gray-600 hover:bg-gray-100"} ${s}`,children:[i?c.jsx(yi,{className:r}):c.jsx(Ti,{className:r}),e&&c.jsx("span",{className:"text-xs",children:i?"Copied!":e})]})}function _r(t){switch(t){case"Active":return{bg:"bg-emerald-100",text:"text-emerald-700",dot:"bg-emerald-500"};case"Scheduled":return{bg:"bg-sky-100",text:"text-sky-700",dot:"bg-sky-500"};case"DeadLettered":return{bg:"bg-red-100",text:"text-red-700",dot:"bg-red-500"};case"Replayed":return{bg:"bg-amber-100",text:"text-amber-700",dot:"bg-amber-500"};case"Resolved":return{bg:"bg-gray-100",text:"text-gray-600",dot:"bg-gray-400"};case"Deferred":return{bg:"bg-purple-100",text:"text-purple-700",dot:"bg-purple-500"};default:return{bg:"bg-gray-100",text:"text-gray-600",dot:"bg-gray-400"}}}function rs(t){try{return new Date(t).toLocaleString("en-US",{month:"short",day:"numeric",year:"numeric",hour:"2-digit",minute:"2-digit",second:"2-digit"})}catch{return t}}function zn(t){var e;return(e=t.entityPath)!=null&&e.includes("/subscriptions/")?"Topic/Sub":"Queue"}const Un=[{label:"Last 1 hour",value:"1h"},{label:"Last 6 hours",value:"6h"},{label:"Last 24 hours",value:"24h"},{label:"Last 7 days",value:"7d"},{label:"All time",value:"all"}];function Hn(t,e){if(e==="all")return t;const s=Date.now(),r={"1h":3600*1e3,"6h":360*60*1e3,"24h":1440*60*1e3,"7d":10080*60*1e3,all:1/0},i=s-r[e];return t.filter(n=>new Date(n.timestamp).getTime()>=i)}function Is(t){const e=new Blob([JSON.stringify(t,null,2)],{type:"application/json"}),s=URL.createObjectURL(e),r=document.createElement("a");r.href=s,r.download=`correlation-${t.correlationId}-${new Date().toISOString().slice(0,19).replace(/:/g,"-")}.json`,r.click(),URL.revokeObjectURL(s)}function Er(t){if(t<1e3)return`${t}ms`;const e=Math.floor(t/1e3);if(e<60)return`${e}s`;const s=Math.floor(e/60);if(s<60)return`${s}m ${e%60}s`;const r=Math.floor(s/60);return r<24?`${r}h ${s%60}m`:`${Math.floor(r/24)}d ${r%24}h`}function Kn({fromTs:t,toTs:e}){const s=new Date(e).getTime()-new Date(t).getTime();return s<=0?null:c.jsxs("div",{className:"flex items-center gap-2 ml-8 my-1 text-xs text-gray-400 select-none",children:[c.jsx("div",{className:"h-px flex-1 border-l-0 border-t border-dashed border-gray-300"}),c.jsxs("span",{className:"shrink-0 px-2 py-0.5 rounded-full bg-gray-100 text-gray-500 font-mono",children:["+",Er(s)]}),c.jsx("div",{className:"h-px flex-1 border-r-0 border-t border-dashed border-gray-300"})]})}function Vn({entries:t}){if(t.length<2)return null;const e=t.map(p=>new Date(p.timestamp).getTime()),s=Math.min(...e),i=Math.max(...e)-s||1,n=100,o=28,l=14,u=3.5;return c.jsxs("div",{className:"mb-5 bg-white border border-gray-200 rounded-xl px-5 py-3 shadow-sm",children:[c.jsxs("div",{className:"flex items-center justify-between mb-1.5",children:[c.jsxs("span",{className:"text-xs font-semibold text-gray-500 uppercase tracking-wide",children:["Timeline Minimap · ",t.length," event",t.length!==1?"s":""]}),c.jsxs("span",{className:"text-xs text-gray-400 font-mono",children:[Er(i)," total span"]})]}),c.jsxs("svg",{viewBox:`0 0 ${n} ${o}`,preserveAspectRatio:"none",className:"w-full",style:{height:o},children:[c.jsx("line",{x1:"2",y1:l,x2:"98",y2:l,stroke:"#e5e7eb",strokeWidth:"1.5"}),t.map((p,y)=>{const b=2+(new Date(p.timestamp).getTime()-s)/i*96,{dot:g}=_r(p.state),w={"bg-emerald-500":"#10b981","bg-sky-500":"#0ea5e9","bg-red-500":"#ef4444","bg-amber-500":"#f59e0b","bg-gray-400":"#9ca3af","bg-purple-500":"#a855f7"}[g]??"#9ca3af";return c.jsx("g",{children:c.jsx("circle",{cx:b,cy:l,r:u,fill:w,opacity:"0.9"})},y)})]}),c.jsxs("div",{className:"flex justify-between text-[10px] text-gray-400 font-mono mt-0.5",children:[c.jsx("span",{children:rs(t[0].timestamp).split(",")[0]}),c.jsx("span",{children:rs(t[t.length-1].timestamp).split(",")[0]})]})]})}function Bn({entry:t,isLast:e,index:s}){const r=_r(t.state),[i,n]=m.useState(!1),o=zn(t);return c.jsxs("div",{className:"flex gap-4",children:[c.jsxs("div",{className:"flex flex-col items-center shrink-0 w-8",children:[c.jsx("span",{className:"text-xs font-bold text-gray-400 text-center leading-none mb-1 pt-3",children:s+1}),c.jsx("div",{className:`w-3 h-3 rounded-full shrink-0 ${r.dot}`}),!e&&c.jsx("div",{className:"w-0.5 flex-1 bg-gray-200 mt-1"})]}),c.jsx("div",{className:"flex-1 mb-4",children:c.jsxs("div",{className:"bg-white border border-gray-200 rounded-xl shadow-sm p-4",children:[c.jsxs("div",{className:"flex items-center justify-between mb-2 gap-2 flex-wrap",children:[c.jsxs("div",{className:"flex items-center gap-2 min-w-0 flex-wrap",children:[c.jsx("span",{className:`text-xs font-semibold px-2 py-0.5 rounded-full shrink-0 ${r.bg} ${r.text}`,children:t.state}),c.jsx("span",{className:"text-sm font-medium text-gray-900 truncate",children:t.entityName}),c.jsx("span",{className:`text-xs px-2 py-0.5 rounded-full shrink-0 font-medium ${o==="Queue"?"bg-sky-50 text-sky-600 border border-sky-100":"bg-indigo-50 text-indigo-600 border border-indigo-100"}`,children:o})]}),c.jsx("span",{className:`text-xs px-2 py-0.5 rounded-full shrink-0 font-medium ${t.source==="Live"?"bg-sky-100 text-sky-700":"bg-gray-100 text-gray-600"}`,children:t.source==="Live"?c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsx(Mr,{className:"w-3 h-3"}),"Live"]}):c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsx(kr,{className:"w-3 h-3"}),"History"]})})]}),c.jsxs("div",{className:"flex items-center gap-4 text-xs text-gray-500 mb-2 flex-wrap",children:[c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsx(Ri,{className:"w-3 h-3"}),rs(t.timestamp)]}),c.jsxs("span",{children:["SeqNo: ",t.sequenceNumber.toLocaleString()]}),c.jsxs("span",{children:["Size: ",t.sizeInBytes>0?`${(t.sizeInBytes/1024).toFixed(1)} KB`:"—"]}),"            ",t.messageId&&c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsxs("span",{className:"font-mono text-gray-500",children:["ID: ",t.messageId.slice(0,8),"…"]}),c.jsx(Sr,{text:t.messageId,label:"message ID",iconSize:"w-3 h-3"})]}),"          "]}),c.jsxs("p",{className:"text-xs text-gray-500 mb-2",children:["Namespace:"," ",c.jsx("span",{className:"font-medium text-gray-700",children:t.namespaceDisplayName}),t.entityPath&&t.entityPath!==t.entityName&&c.jsxs("span",{className:"ml-1 text-gray-400",children:["(",t.entityPath,")"]})]}),t.bodyPreview&&c.jsxs("div",{className:"mt-1",children:[c.jsx("div",{className:`text-xs text-gray-600 font-mono bg-gray-50 border border-gray-100 rounded px-2 py-1.5 ${i?"whitespace-pre-wrap break-words":"truncate"}`,children:i?t.bodyPreview:t.bodyPreview.slice(0,200)+(t.bodyPreview.length>200?"…":"")}),t.bodyPreview.length>200&&c.jsx("button",{onClick:()=>n(l=>!l),className:"mt-1 text-xs text-violet-600 hover:text-violet-800 flex items-center gap-1",children:i?c.jsxs(c.Fragment,{children:[c.jsx(ki,{className:"w-3 h-3"})," Show less"]}):c.jsxs(c.Fragment,{children:[c.jsx(gi,{className:"w-3 h-3"})," Show full body"]})})]}),t.deadLetterReason&&c.jsxs("p",{className:"text-xs text-red-600 mt-2 flex items-center gap-1",children:[c.jsx(Cr,{className:"w-3 h-3 shrink-0"}),"DLQ Reason: ",t.deadLetterReason]})]})})]})}function Gn(){return c.jsxs("div",{className:"flex flex-col items-center justify-center h-full text-center px-8 py-16",children:[c.jsx(wr,{className:"w-14 h-14 text-gray-300 mb-4"}),c.jsx("p",{className:"text-gray-600 font-semibold text-lg mb-1",children:"Enter a Correlation ID"}),c.jsx("p",{className:"text-gray-400 text-sm max-w-sm",children:"Trace a message journey across all your queues and namespaces by entering a Correlation ID above."}),c.jsx("div",{className:"mt-6 grid grid-cols-3 gap-3 text-left w-full max-w-sm",children:[{icon:"🔍",title:"Cross-namespace",body:"Searches all connected namespaces in parallel"},{icon:"📜",title:"Live + History",body:"Merges live queue data with DLQ history"},{icon:"📦",title:"Full journey",body:"Shows message state at every hop"}].map(t=>c.jsxs("div",{className:"bg-white border border-gray-100 rounded-xl p-3",children:[c.jsx("div",{className:"text-lg mb-1",children:t.icon}),c.jsx("p",{className:"text-xs font-semibold text-gray-700 mb-0.5",children:t.title}),c.jsx("p",{className:"text-xs text-gray-400",children:t.body})]},t.title))})]})}function Fs(){const[t,e]=Or(),s=t.get("correlationId")??"",r=t.get("namespaceId")??"",[i,n]=m.useState(s),[o,l]=m.useState(r),[u,p]=m.useState("all"),[y,h]=m.useState("all"),[b,g]=m.useState(!1),{data:N}=Dn(),w=qn(),j=m.useRef(!1);m.useEffect(()=>{s&&!j.current&&(j.current=!0,w.mutate({correlationId:s,namespaceId:r||void 0}))},[]);function S(){if(!i.trim())return;const v={correlationId:i.trim()};o&&(v.namespaceId=o),e(v),w.mutate({correlationId:i.trim(),namespaceId:o||void 0})}function O(v){v.key==="Enter"&&S()}const k=w.data,M=w.isPending,I=w.isSuccess||w.isError,P=m.useMemo(()=>{if(!(k!=null&&k.entries))return[];let v=Hn(k.entries,u);return y==="queue"?v=v.filter(R=>{var A;return!((A=R.entityPath)!=null&&A.includes("/subscriptions/"))}):y==="topic"&&(v=v.filter(R=>{var A;return(A=R.entityPath)==null?void 0:A.includes("/subscriptions/")})),v},[k==null?void 0:k.entries,u,y]),K=u!=="all"||y!=="all";return c.jsxs("div",{className:"flex-1 flex flex-col overflow-hidden",children:[c.jsx("div",{className:"bg-gradient-to-r from-violet-600 to-violet-500 px-6 py-4 shrink-0",children:c.jsxs("div",{className:"flex items-center justify-between",children:[c.jsxs("div",{className:"flex items-center gap-3",children:[c.jsx(wr,{className:"w-6 h-6 text-white/80"}),c.jsxs("div",{children:[c.jsx("h1",{className:"text-xl font-semibold text-white",children:"Correlation Explorer"}),c.jsx("p",{className:"text-violet-100 text-sm",children:"Trace any message's full journey across all queues and namespaces"})]})]}),k&&k.totalCount>0&&c.jsxs("button",{onClick:()=>Is(k),className:"flex items-center gap-2 px-3 py-1.5 bg-white/20 hover:bg-white/30 text-white rounded-lg text-sm font-medium transition-colors",title:"Export timeline as JSON",children:[c.jsx(Ps,{className:"w-4 h-4"}),"Export JSON"]})]})}),c.jsxs("div",{className:"bg-white border-b border-gray-200 px-3 sm:px-4 lg:px-6 py-3 shrink-0 overflow-x-auto",children:[c.jsxs("div",{className:"flex items-center gap-2 sm:gap-3 flex-wrap",children:[c.jsxs("div",{className:"flex-1 flex items-center gap-2 bg-gray-50 border border-gray-300 rounded-lg px-3 py-2 focus-within:border-violet-400 focus-within:ring-1 focus-within:ring-violet-400 transition-all",children:[c.jsx(Ts,{className:"w-4 h-4 text-gray-400 shrink-0"}),c.jsx("input",{type:"text",value:i,onChange:v=>n(v.target.value),onKeyDown:O,placeholder:"Enter Correlation ID…",className:"flex-1 bg-transparent text-sm text-gray-900 placeholder-gray-400 outline-none","aria-label":"Correlation ID"})]}),c.jsxs("select",{value:o,onChange:v=>l(v.target.value),className:"text-sm border border-gray-300 rounded-lg px-3 py-2 bg-white text-gray-700 focus:border-violet-400 focus:outline-none focus:ring-1 focus:ring-violet-400","aria-label":"Namespace filter",children:[c.jsx("option",{value:"",children:"All Namespaces"}),N==null?void 0:N.map(v=>c.jsx("option",{value:v.id,children:v.displayName??v.name},v.id))]}),c.jsxs("button",{onClick:()=>g(v=>!v),className:`flex items-center gap-1.5 px-2 sm:px-3 py-2 border rounded-lg text-xs sm:text-sm font-medium transition-colors ${K?"border-violet-400 bg-violet-50 text-violet-700":"border-gray-300 bg-white text-gray-700 hover:bg-gray-50"}`,"aria-label":"Toggle result filters",children:[c.jsx($s,{className:"w-4 h-4"}),c.jsx("span",{className:"hidden sm:inline",children:"Filters"}),K&&c.jsx("span",{className:"w-2 h-2 rounded-full bg-violet-500 ml-0.5"})]}),c.jsxs("button",{onClick:S,disabled:!i.trim()||M,className:"flex items-center gap-1.5 px-3 sm:px-4 py-2 bg-violet-600 hover:bg-violet-700 disabled:bg-violet-300 text-white rounded-lg text-xs sm:text-sm font-medium transition-colors whitespace-nowrap",children:[c.jsx(Ts,{className:"w-4 h-4"}),c.jsx("span",{className:"hidden sm:inline",children:M?"Searching…":"Search"}),c.jsx("span",{className:"sm:hidden",children:M?"...":"→"})]})]}),b&&c.jsxs("div",{className:"flex items-center gap-2 sm:gap-4 mt-3 pt-3 border-t border-gray-100 flex-wrap",children:[c.jsxs("div",{className:"flex items-center gap-2",children:[c.jsx("label",{className:"text-xs font-medium text-gray-600",children:"Time range"}),c.jsx("select",{value:u,onChange:v=>p(v.target.value),className:"text-sm border border-gray-200 rounded-lg px-2.5 py-1 bg-white focus:border-violet-400 focus:outline-none focus:ring-1 focus:ring-violet-400",children:Un.map(v=>c.jsx("option",{value:v.value,children:v.label},v.value))})]}),c.jsxs("div",{className:"flex items-center gap-2",children:[c.jsx("label",{className:"text-xs font-medium text-gray-600",children:"Entity type"}),c.jsx("div",{className:"flex rounded-lg border border-gray-200 overflow-hidden",children:["all","queue","topic"].map(v=>c.jsx("button",{onClick:()=>h(v),className:`px-3 py-1 text-xs font-medium transition-colors ${y===v?"bg-violet-600 text-white":"bg-white text-gray-600 hover:bg-gray-50"}`,children:v==="all"?"All":v==="queue"?"Queues":"Topics"},v))})]}),K&&c.jsx("button",{onClick:()=>{p("all"),h("all")},className:"text-xs text-gray-400 hover:text-gray-600 underline",children:"Clear filters"})]})]}),c.jsx("div",{className:"flex-1 overflow-auto bg-gray-50",children:M?c.jsxs("div",{className:"flex flex-col items-center justify-center h-full gap-3 text-gray-500",children:[c.jsx("div",{className:"animate-spin rounded-full border-4 border-violet-200 border-t-violet-600 w-10 h-10"}),c.jsxs("p",{className:"text-sm",children:["Searching across ",(N==null?void 0:N.length)??"…"," namespace(s)…"]})]}):I?k&&k.totalCount===0?c.jsxs("div",{className:"flex flex-col items-center justify-center h-full text-center px-8 py-16",children:[c.jsx(Ci,{className:"w-12 h-12 text-gray-300 mb-4"}),c.jsx("p",{className:"text-gray-600 font-semibold text-lg mb-1",children:"No messages found"}),c.jsxs("p",{className:"text-gray-400 text-sm",children:["No messages for correlation ID:"," ",c.jsx("span",{className:"font-mono text-gray-600",children:k.correlationId})]})]}):k?c.jsxs("div",{className:"px-3 sm:px-4 lg:px-6 py-4 sm:py-5 w-full max-w-5xl mx-auto",children:[k.isPartialResult&&c.jsxs("div",{className:"flex items-center gap-2 bg-amber-50 border border-amber-200 rounded-lg px-4 py-2.5 mb-4 text-amber-800 text-sm",children:[c.jsx(Cr,{className:"w-4 h-4 shrink-0 text-amber-600"}),c.jsxs("span",{children:["Search timed out — showing partial results (",k.totalCount," entries found)"]})]}),c.jsxs("div",{className:"flex items-center justify-between mb-4 flex-wrap gap-2",children:[c.jsxs("div",{children:[c.jsxs("p",{className:"text-gray-700 text-sm",children:["Found"," ",c.jsx("span",{className:"font-semibold text-gray-900",children:k.totalCount})," ","message(s) across"," ",c.jsx("span",{className:"font-semibold",children:k.entitiesSearched})," entity/ies in"," ",c.jsx("span",{className:"font-semibold",children:k.namespacesSearched})," namespace(s)"]}),c.jsxs("p",{className:"text-xs text-gray-400 mt-0.5 flex items-center gap-1.5",children:["Search completed in ",k.searchDurationMs.toLocaleString(),"ms",K&&P.length!==k.entries.length&&c.jsxs("span",{className:"ml-2 text-violet-600 font-medium",children:["· Showing ",P.length," of ",k.totalCount," after filters"]}),c.jsxs("span",{className:"flex items-center gap-1 ml-2 font-mono text-gray-500",children:["CorrelationID: ",k.correlationId.slice(0,12),"…",c.jsx(Sr,{text:k.correlationId,label:"correlation ID",iconSize:"w-3 h-3"})]})]})]}),c.jsxs("button",{onClick:()=>Is(k),className:"flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-violet-700 bg-violet-50 hover:bg-violet-100 border border-violet-200 rounded-lg transition-colors",children:[c.jsx(Ps,{className:"w-3.5 h-3.5"}),"Export JSON"]})]}),c.jsxs("div",{className:"flex items-center gap-4 mb-4 text-xs text-gray-500",children:[c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsx(Mr,{className:"w-3 h-3 text-sky-500"}),"Live = currently in queue"]}),c.jsxs("span",{className:"flex items-center gap-1",children:[c.jsx(kr,{className:"w-3 h-3 text-gray-400"}),"History = from DLQ history database"]})]}),P.length===0?c.jsxs("div",{className:"text-center py-12 text-gray-400",children:[c.jsx($s,{className:"w-10 h-10 mx-auto mb-3 opacity-40"}),c.jsx("p",{className:"font-medium",children:"No entries match the current filters"}),c.jsx("button",{onClick:()=>{p("all"),h("all")},className:"mt-2 text-sm text-violet-600 underline",children:"Clear filters"})]}):c.jsxs(c.Fragment,{children:[c.jsx(Vn,{entries:P}),c.jsx("div",{className:"ml-1 sm:ml-2 -mr-4",children:P.map((v,R)=>c.jsxs("div",{children:[R>0&&c.jsx(Kn,{fromTs:P[R-1].timestamp,toTs:v.timestamp}),c.jsx(Bn,{entry:v,isLast:R===P.length-1,index:R})]},`${v.messageId}-${R}`))})]})]}):null:c.jsx(Gn,{})})]})}const gc=Object.freeze(Object.defineProperty({__proto__:null,CorrelationExplorerPage:Fs,default:Fs},Symbol.toStringTag,{value:"Module"}));export{Bo as $,so as A,kr as B,Ri as C,Ps as D,_o as E,Oo as F,To as G,cc as H,Io as I,ec as J,Fo as K,zo as L,Uo as M,Vo as N,lo as O,Yo as P,uc as Q,Xo as R,sc as S,lc as T,hc as U,co as V,dc as W,pc as X,io as Y,fc as Z,jo as _,la as a,yi as a0,Lo as a1,rc as a2,Ho as a3,Wo as a4,Do as a5,oo as a6,bo as a7,Ln as a8,Ti as a9,Xn as aA,to as aB,gc as aC,ac as aa,Sr as ab,Mo as ac,qo as ad,Zo as ae,ko as af,Go as ag,yc as ah,mc as ai,So as aj,ic as ak,$o as al,oc as am,nc as an,Po as ao,Jo as ap,Ao as aq,Co as ar,tc as as,no as at,uo as au,ho as av,Ko as aw,Eo as ax,No as ay,Yn as az,le as b,Dn as c,wr as d,fo as e,Cr as f,Ci as g,yo as h,mo as i,c as j,xo as k,mt as l,ds as m,Ro as n,ao as o,vo as p,go as q,ro as r,po as s,$s as t,eo as u,wo as v,Ts as w,gi as x,Qo as y,Y as z};
