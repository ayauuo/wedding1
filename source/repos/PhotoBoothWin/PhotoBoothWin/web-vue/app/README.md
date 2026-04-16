# app (PhotoBooth)

本專案已將 `web/` 內 PhotoBooth 的 HTML/JS/CSS 遷移至 Vue 3，組件化並使用 SCSS。

- **畫面元件**：`src/components/photobooth/`（ScreenIdle、ScreenTemplate、ScreenResult、ScreenProcessing）；拍照與濾鏡在 `photobooth/shoot/`（ScreenShoot、FilterOptions）
- **共用邏輯**：`src/composables/usePhotobooth.ts`、`useHost.ts`、`useTakePicture.ts`
- **樣式**：全域 `src/styles/`（`_variables.scss`、`_mixins.scss`、`_base.scss`、`_loading.scss`），各元件內 `<style lang="scss" scoped>`

請先執行 `npm install`（含 `sass`、`sass-embedded`）後再執行 `npm run dev` 或 `npm run build`。

## Recommended IDE Setup

[VS Code](https://code.visualstudio.com/) + [Vue (Official)](https://marketplace.visualstudio.com/items?itemName=Vue.volar) (and disable Vetur).

## Recommended Browser Setup

- Chromium-based browsers (Chrome, Edge, Brave, etc.):
  - [Vue.js devtools](https://chromewebstore.google.com/detail/vuejs-devtools/nhdogjmejiglipccpnnnanhbledajbpd)
  - [Turn on Custom Object Formatter in Chrome DevTools](http://bit.ly/object-formatters)
- Firefox:
  - [Vue.js devtools](https://addons.mozilla.org/en-US/firefox/addon/vue-js-devtools/)
  - [Turn on Custom Object Formatter in Firefox DevTools](https://fxdx.dev/firefox-devtools-custom-object-formatters/)

## Type Support for `.vue` Imports in TS

TypeScript cannot handle type information for `.vue` imports by default, so we replace the `tsc` CLI with `vue-tsc` for type checking. In editors, we need [Volar](https://marketplace.visualstudio.com/items?itemName=Vue.volar) to make the TypeScript language service aware of `.vue` types.

## Customize configuration

See [Vite Configuration Reference](https://vite.dev/config/).

## Project Setup

```sh
npm install
```

### Compile and Hot-Reload for Development

```sh
npm run dev
```

### Type-Check, Compile and Minify for Production

```sh
npm run build
```

### Run Unit Tests with [Vitest](https://vitest.dev/)

```sh
npm run test:unit
```

### Lint with [ESLint](https://eslint.org/)

```sh
npm run lint
```
