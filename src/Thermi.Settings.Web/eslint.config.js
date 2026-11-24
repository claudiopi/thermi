import js from '@eslint/js';
import reactHooks from 'eslint-plugin-react-hooks';
import { defineConfig } from 'eslint/config';
import tseslint from 'typescript-eslint';

// Texts come from Core's translations through the bridge, never from the page's code: no text between tags, no
// literal string as a child, and no literal in the attributes Narrator or a person reads.
const noText = 'Every text a user reads comes from the translations: take it with useText, by its key.';
const child = ':matches(JSXElement, JSXFragment) > JSXExpressionContainer';
const readAttributes = /^(aria-label|aria-description|aria-valuetext|aria-roledescription|title|placeholder|alt|label|description|text|stateText|valueText|main|cancel)$/;

export default defineConfig(
  { ignores: ['dist/', 'node_modules/', 'test-results/', 'playwright-report/'] },
  js.configs.recommended,
  tseslint.configs.recommended,
  {
    // A rest pattern that leaves fields out names them, unused, on purpose.
    rules: { '@typescript-eslint/no-unused-vars': ['error', { ignoreRestSiblings: true }] },
  },
  {
    files: ['src/**/*.{ts,tsx}'],
    extends: [reactHooks.configs.flat.recommended],
    rules: {
      // Fluent is used only through the kit, so replacing it on another platform touches only src/kit.
      'no-restricted-imports': [
        'error',
        { patterns: [{ group: ['@fluentui/*', '@griffel/*'], message: 'Use Fluent through the kit in src/kit.' }] },
      ],
      'no-restricted-syntax': [
        'error',
        { selector: 'JSXText[value=/\\S/]', message: noText },
        { selector: `${child} > Literal[value=/\\S/]`, message: noText },
        { selector: `${child} > :matches(ConditionalExpression, LogicalExpression) > Literal[value=/\\S/]`, message: noText },
        { selector: `${child} > TemplateLiteral > TemplateElement[value.raw=/\\S/]`, message: noText },
        { selector: `JSXAttribute[name.name=${readAttributes}] > Literal`, message: noText },
      ],
    },
  },
  {
    files: ['src/kit/**/*.{ts,tsx}'],
    rules: { 'no-restricted-imports': 'off' },
  },
);
