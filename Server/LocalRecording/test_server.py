import unittest
from unittest.mock import patch
from fastapi.testclient import TestClient
import server


class RecordingServerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Only omit secret loading. No network/model provider is replaced or called.
        with patch.object(server, 'load_settings'):
            cls.app = server.create_app(server.ROOT / 'Builds/ServerReview/sushisite/sushi-fast')

    def test_real_session_contract_and_auth(self):
        with TestClient(self.app) as client:
            health = client.get('/health').json()
            self.assertFalse(health['mock'])
            response = client.post('/odi/xreal_rehear/evc/smart-start', data={
                'presentation_title': 'Local route verification', 'seed': '7',
                'independent_reactions': 'true'})
            self.assertEqual(response.status_code, 200, response.text)
            session = response.json()
            path = '/odi/xreal_rehear/evc/sessions/' + session['session_id']
            headers = {'X-EVC-Session-Token': session['session_token']}
            self.assertNotEqual(client.get(path).status_code, 200)
            record = client.get(path, headers=headers)
            self.assertEqual(record.status_code, 200)
            self.assertEqual(len(record.json()['audiences']), 6)
            self.assertEqual(client.delete(path, headers=headers).status_code, 204)

    def test_speech_and_answer_routes_exist(self):
        paths = self.app.openapi()['paths']
        for suffix in ('speech', 'answer'):
            self.assertIn('post', paths['/odi/xreal_rehear/evc/sessions/{session_id}/questions/{index}/' + suffix])


if __name__ == '__main__':
    unittest.main()
