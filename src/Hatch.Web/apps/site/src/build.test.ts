/// <reference types="node" />
import { describe, expect, it } from 'vitest'
import { buildSite } from './testing/buildSite'

describe('build', () => {
  it('writes index.html and 404.html, each with a heading', async () => {
    const { read } = await buildSite()

    expect(read('index.html')).toMatch(/<h1>/)
    expect(read('404.html')).toMatch(/<h1>/)
  })
})
