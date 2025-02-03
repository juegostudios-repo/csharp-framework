POST http://localhost:3000/user/login
{
    "type": 1,
    "user_name": "test"
}
HTTP 200
[Captures]
access_token: jsonpath "$.responseData.access_token"

POST http://localhost:3000/user/logout
access_token: {{access_token}}
