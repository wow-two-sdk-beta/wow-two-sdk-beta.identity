import { mount } from '@vue/test-utils';
import { afterEach } from 'vitest';

const wrappers: ReturnType<typeof mount>[] = [];

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
});

/** Mounts a kit component into the document, so focus and form submission behave as in the browser. */
export function render(...args: Parameters<typeof mount>) {
  const [component, options] = args;
  const wrapper = mount(component, { attachTo: document.body, ...options });
  wrappers.push(wrapper);
  return wrapper;
}

/** Finds the button whose text is `text`. */
export function button(wrapper: ReturnType<typeof mount>, text: string) {
  const found = wrapper.findAll('button').find((candidate) => candidate.text() === text);
  if (!found) throw new Error(`No button "${text}" in: ${wrapper.text()}`);
  return found;
}
