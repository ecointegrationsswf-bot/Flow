import tsParser from '@typescript-eslint/parser'

// Config ESLint MÍNIMA, a propósito: solo prohíbe alert/confirm/prompt nativos.
// No habilita el resto del set "recomendado" para no inundar el build con
// warnings preexistentes. Si en el futuro se quiere lint completo, ampliar acá.
export default [
  {
    ignores: ['dist/**', 'node_modules/**', 'eslint.config.mjs', 'vite.config.*'],
  },
  {
    files: ['src/**/*.{ts,tsx}'],
    // Ignora TODOS los comentarios inline de ESLint. Doble beneficio:
    //  1) No rompe por directivos `eslint-disable` preexistentes que apuntan a
    //     reglas de plugins que esta config mínima no carga.
    //  2) Nadie puede saltar `no-alert` con un `// eslint-disable-line no-alert`
    //     → la prohibición de alert/confirm/prompt es absoluta.
    linterOptions: { noInlineConfig: true },
    languageOptions: {
      parser: tsParser,
      parserOptions: {
        ecmaVersion: 'latest',
        sourceType: 'module',
        ecmaFeatures: { jsx: true },
      },
    },
    rules: {
      // 🚫 PROHIBIDO TERMINANTEMENTE: alert / confirm / prompt nativos
      // (incluye window.alert / window.confirm / window.prompt).
      // Usar confirmDialog / alertDialog / promptDialog / toast de
      // src/shared/components/dialog.tsx en su lugar.
      'no-alert': 'error',
    },
  },
]
