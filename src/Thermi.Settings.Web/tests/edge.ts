// How the tests launch Edge, in the config and in the specs that change its launch. Without the GPU, a run's browsers
// draw in software and take nothing of the graphics the user's desktop draws with.
export const edge = { args: ['--disable-gpu'] };
