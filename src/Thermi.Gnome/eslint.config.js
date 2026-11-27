// The extension's lint, run by the check with the page's ESLint; the repo's rules hold its lines and length. It imports
// nothing, so it needs no packages of its own: GNOME Shell loads the extension as it is, with no build.
export default [
    {
        files: ['**/*.js'],
        languageOptions: {
            ecmaVersion: 2024,
            sourceType: 'module',
            globals: { global: 'readonly', console: 'readonly' },
        },
        rules: {
            'no-undef': 'error',
            'no-unused-vars': ['error', { args: 'none' }],
            'no-var': 'error',
            'prefer-const': 'error',
            'eqeqeq': 'error',
            'no-shadow': 'error',
            'no-implicit-globals': 'error',
            'no-unreachable': 'error',
            'no-dupe-keys': 'error',
            'no-empty': 'error',
            'curly': ['error', 'multi-or-nest', 'consistent'],
        },
    },
];
