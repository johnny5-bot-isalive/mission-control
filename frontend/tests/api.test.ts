import assert from 'node:assert/strict'
import test from 'node:test'

import { buildApiUrl } from '../src/api.ts'

test('buildApiUrl avoids duplicate slashes for trailing-slash API bases', () => {
  assert.equal(
    buildApiUrl('/api/', '/actions/create-project/preview'),
    '/api/actions/create-project/preview',
  )

  assert.equal(
    buildApiUrl('http://localhost:5181/api/', '/actions/create-project'),
    'http://localhost:5181/api/actions/create-project',
  )
})
