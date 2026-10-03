/** Shape of the generated step-by-step guides (scripts/docs-shots/build_guides.py). */
export interface GuideKey { readonly n: number; readonly text: string }
export interface GuideShot { readonly image: string; readonly alt: string; readonly keys: readonly GuideKey[] }
export interface GuideStep { readonly title: string; readonly text: string; readonly shots: readonly GuideShot[] }
export interface GuideSection { readonly part: string; readonly steps: readonly GuideStep[] }
export interface Guide {
  readonly cloud: string
  readonly title: string
  readonly sections: readonly GuideSection[]
  readonly troubleshooting: readonly { readonly q: string; readonly a: string }[]
}
